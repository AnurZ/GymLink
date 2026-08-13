using System.Globalization;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using GymLink.Application.Abstractions;
using GymLink.Application.Administration;
using GymLink.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GymLink.Infrastructure.Geocoding;

internal sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string UserAgent { get; init; } = string.Empty;
    public string? ContactEmail { get; init; }
    public int TimeoutSeconds { get; init; } = 10;
    public int CacheHours { get; init; } = 24;
    public int MinimumIntervalMilliseconds { get; init; } = 1000;

    public bool IsValid() =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(UserAgent) &&
        !IsPlaceholder(UserAgent) &&
        (string.IsNullOrWhiteSpace(ContactEmail) ||
         (!IsPlaceholder(ContactEmail) && MailAddress.TryCreate(ContactEmail, out _))) &&
        TimeoutSeconds is > 0 and <= 60 &&
        CacheHours is > 0 and <= 168 &&
        MinimumIntervalMilliseconds >= 1000;

    private static bool IsPlaceholder(string value) =>
        value.Contains("replace-with", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("example.com", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("changeme", StringComparison.OrdinalIgnoreCase);
}

internal sealed class NominatimLocationSearchService(
    IHttpClientFactory httpClientFactory,
    IApplicationDbContext dbContext,
    IMemoryCache cache,
    IOptions<GeocodingOptions> options,
    TimeProvider timeProvider,
    ILogger<NominatimLocationSearchService> logger) : ILocationSearchService
{
    private const int ResultLimit = 8;
    private static readonly SemaphoreSlim UpstreamLock = new(1, 1);
    private static DateTimeOffset _lastUpstreamRequest = DateTimeOffset.MinValue;
    private static readonly Action<ILogger, string, int, Exception?> LogProviderStatus =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(7101, "NominatimStatus"),
            "Nominatim {Operation} failed with HTTP status {StatusCode}.");
    private static readonly Action<ILogger, string, Exception?> LogProviderUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(7102, "NominatimUnavailable"),
            "Nominatim {Operation} timed out or was unavailable.");
    private readonly GeocodingOptions _settings = options.Value;

    public async Task<IReadOnlyList<LocationSearchResultDto>> SearchAsync(
        LocationSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            throw Unavailable();
        }

        var query = request.Query.Trim();
        var cacheKey = $"geocoding:nominatim:{Normalize(query)}";
        var providerResults = await cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_settings.CacheHours);
                return await SearchProviderAsync(query, cancellationToken);
            }) ?? [];

        var cities = await LoadCitiesAsync(cancellationToken);

        return providerResults
            .Select(result => MapResult(result, cities))
            .Where(result => result is not null)
            .Cast<LocationSearchResultDto>()
            .Take(ResultLimit)
            .ToArray();
    }

    public async Task<LocationReverseResultDto> ReverseAsync(
        LocationReverseRequest request,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            throw Unavailable();
        }

        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;
        var roundedLatitude = decimal.Round(latitude, 5, MidpointRounding.AwayFromZero);
        var roundedLongitude = decimal.Round(longitude, 5, MidpointRounding.AwayFromZero);
        var cacheKey =
            $"geocoding:nominatim:reverse:{roundedLatitude.ToString(CultureInfo.InvariantCulture)}:"
            + roundedLongitude.ToString(CultureInfo.InvariantCulture);
        var providerResult = await cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_settings.CacheHours);
                return await ReverseProviderAsync(
                    roundedLatitude,
                    roundedLongitude,
                    cancellationToken);
            });
        if (providerResult is null)
        {
            throw NotResolved();
        }

        return MapReverseResult(
            providerResult,
            await LoadCitiesAsync(cancellationToken),
            roundedLatitude,
            roundedLongitude);
    }

    private async Task<IReadOnlyList<CityCandidate>> LoadCitiesAsync(
        CancellationToken cancellationToken) =>
        await (
                from city in dbContext.Cities.AsNoTracking()
                join country in dbContext.Countries.AsNoTracking()
                    on city.CountryId equals country.Id
                where city.IsActive && country.IsActive && country.Code == "BIH"
                select new CityCandidate(city.Id, city.Name))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<NominatimResult>> SearchProviderAsync(
        string query,
        CancellationToken cancellationToken)
    {
        await UpstreamLock.WaitAsync(cancellationToken);
        try
        {
            var elapsed = timeProvider.GetUtcNow() - _lastUpstreamRequest;
            var minimumInterval = TimeSpan.FromMilliseconds(_settings.MinimumIntervalMilliseconds);
            if (elapsed < minimumInterval)
            {
                await Task.Delay(minimumInterval - elapsed, timeProvider, cancellationToken);
            }

            _lastUpstreamRequest = timeProvider.GetUtcNow();
            var parameters = new Dictionary<string, string?>
            {
                ["q"] = query,
                ["format"] = "jsonv2",
                ["limit"] = ResultLimit.ToString(CultureInfo.InvariantCulture),
                ["addressdetails"] = "1",
                ["countrycodes"] = "ba",
                ["layer"] = "address",
                ["accept-language"] = "bs",
                ["email"] = string.IsNullOrWhiteSpace(_settings.ContactEmail)
                    ? null
                    : _settings.ContactEmail.Trim(),
            };
            var queryString = string.Join(
                "&",
                parameters
                    .Where(pair => pair.Value is not null)
                    .Select(pair =>
                        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
            var endpoint = new Uri(
                new Uri(_settings.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
                $"search?{queryString}");
            using var response = await httpClientFactory.CreateClient("Nominatim")
                .GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogProviderStatus(logger, "search", (int)response.StatusCode, null);
                throw Unavailable();
            }

            return await response.Content.ReadFromJsonAsync<NominatimResult[]>(
                    cancellationToken: cancellationToken)
                ?? [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogProviderUnavailable(logger, "search", null);
            throw Unavailable();
        }
        catch (ExternalServiceUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            LogProviderUnavailable(logger, "search", exception);
            throw new ExternalServiceUnavailableException(
                "location_search_unavailable",
                "Location search is temporarily unavailable. Try again.",
                exception);
        }
        finally
        {
            UpstreamLock.Release();
        }
    }

    private async Task<NominatimResult?> ReverseProviderAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken)
    {
        await UpstreamLock.WaitAsync(cancellationToken);
        try
        {
            var elapsed = timeProvider.GetUtcNow() - _lastUpstreamRequest;
            var minimumInterval = TimeSpan.FromMilliseconds(_settings.MinimumIntervalMilliseconds);
            if (elapsed < minimumInterval)
            {
                await Task.Delay(minimumInterval - elapsed, timeProvider, cancellationToken);
            }

            _lastUpstreamRequest = timeProvider.GetUtcNow();
            var parameters = new Dictionary<string, string?>
            {
                ["lat"] = latitude.ToString(CultureInfo.InvariantCulture),
                ["lon"] = longitude.ToString(CultureInfo.InvariantCulture),
                ["format"] = "jsonv2",
                ["addressdetails"] = "1",
                ["zoom"] = "18",
                ["layer"] = "address",
                ["accept-language"] = "bs",
                ["email"] = string.IsNullOrWhiteSpace(_settings.ContactEmail)
                    ? null
                    : _settings.ContactEmail.Trim(),
            };
            var queryString = string.Join(
                "&",
                parameters
                    .Where(pair => pair.Value is not null)
                    .Select(pair =>
                        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
            var endpoint = new Uri(
                new Uri(_settings.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
                $"reverse?{queryString}");
            using var response = await httpClientFactory.CreateClient("Nominatim")
                .GetAsync(endpoint, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogProviderStatus(logger, "reverse", (int)response.StatusCode, null);
                throw Unavailable();
            }

            return await response.Content.ReadFromJsonAsync<NominatimResult>(
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogProviderUnavailable(logger, "reverse", null);
            throw Unavailable();
        }
        catch (ExternalServiceUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            LogProviderUnavailable(logger, "reverse", exception);
            throw new ExternalServiceUnavailableException(
                "location_search_unavailable",
                "Location search is temporarily unavailable. Try again.",
                exception);
        }
        finally
        {
            UpstreamLock.Release();
        }
    }

    private static LocationSearchResultDto? MapResult(
        NominatimResult result,
        IReadOnlyList<CityCandidate> cities)
    {
        if (!decimal.TryParse(
                result.Latitude,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var latitude) ||
            !decimal.TryParse(
                result.Longitude,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var longitude) ||
            latitude is < -90 or > 90 ||
            longitude is < -180 or > 180 ||
            string.IsNullOrWhiteSpace(result.DisplayName) ||
            !string.Equals(result.Address?.CountryCode, "ba", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var address = result.Address;
        var hierarchy = new[]
        {
            address?.City,
            address?.Town,
            address?.Municipality,
            address?.Village,
            address?.County,
            address?.StateDistrict,
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
        var city = ResolveCity(hierarchy, result.DisplayName, address?.State, cities);
        if (city is null)
        {
            return null;
        }

        var key = string.IsNullOrWhiteSpace(result.OsmType) || result.OsmId is null
            ? $"{latitude.ToString(CultureInfo.InvariantCulture)},{longitude.ToString(CultureInfo.InvariantCulture)}"
            : $"{result.OsmType}:{result.OsmId.Value.ToString(CultureInfo.InvariantCulture)}";
        return new(
            key,
            result.DisplayName.Trim(),
            result.DisplayName.Trim(),
            city.Id,
            city.Name,
            latitude,
            longitude);
    }

    private static LocationReverseResultDto MapReverseResult(
        NominatimResult result,
        IReadOnlyList<CityCandidate> cities,
        decimal latitude,
        decimal longitude)
    {
        if (string.IsNullOrWhiteSpace(result.DisplayName) || result.Address is null)
        {
            throw NotResolved();
        }

        if (string.IsNullOrWhiteSpace(result.Address.CountryCode))
        {
            throw NotResolved();
        }

        if (!string.Equals(
                result.Address.CountryCode,
                "ba",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationRuleException(
                "location_outside_bih",
                "The selected location must be in Bosnia and Herzegovina.");
        }

        var hierarchy = new[]
        {
            result.Address.City,
            result.Address.Town,
            result.Address.Municipality,
            result.Address.Village,
            result.Address.County,
            result.Address.StateDistrict,
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
        var city = ResolveCity(
            hierarchy,
            result.DisplayName,
            result.Address.State,
            cities);
        if (city is null)
        {
            throw NotResolved();
        }

        var key = string.IsNullOrWhiteSpace(result.OsmType) || result.OsmId is null
            ? $"{latitude.ToString(CultureInfo.InvariantCulture)},{longitude.ToString(CultureInfo.InvariantCulture)}"
            : $"{result.OsmType}:{result.OsmId.Value.ToString(CultureInfo.InvariantCulture)}";
        return new(
            key,
            result.DisplayName.Trim(),
            result.DisplayName.Trim(),
            city.Id,
            city.Name);
    }

    private static CityCandidate? ResolveCity(
        IReadOnlyList<string> hierarchy,
        string displayName,
        string? state,
        IReadOnlyList<CityCandidate> cities)
    {
        foreach (var value in hierarchy)
        {
            var normalized = NormalizeAdministrativeName(RemoveQualifier(value));
            var matches = cities
                .Where(city =>
                    NormalizeAdministrativeName(RemoveQualifier(city.Name)) == normalized)
                .ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }

            if (matches.Length > 1)
            {
                var context = Normalize($"{state} {displayName}");
                var qualifier = AdministrativeEntity(context);
                if (qualifier is not null)
                {
                    var qualified = matches.SingleOrDefault(city =>
                        AdministrativeEntity(Normalize(city.Name)) == qualifier);
                    if (qualified is not null)
                    {
                        return qualified;
                    }
                }
            }
        }

        return null;
    }

    private static string? AdministrativeEntity(string normalizedValue)
    {
        if (normalizedValue.Contains("republika srpska", StringComparison.Ordinal))
        {
            return "rs";
        }

        if (normalizedValue.Contains("federacija bih", StringComparison.Ordinal) ||
            normalizedValue.Contains("federacija bosne i hercegovine", StringComparison.Ordinal))
        {
            return "fbih";
        }

        return null;
    }

    private static string RemoveQualifier(string value)
    {
        var index = value.IndexOf('(');
        return index < 0 ? value : value[..index];
    }

    private static string NormalizeAdministrativeName(string value)
    {
        var normalized = Normalize(value);
        foreach (var prefix in new[] { "grad ", "opcina ", "opstina ", "kanton " })
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                return normalized[prefix.Length..].Trim();
            }
        }

        return normalized;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static ExternalServiceUnavailableException Unavailable() =>
        new(
            "location_search_unavailable",
            "Location search is temporarily unavailable. Try again.");

    private static NotFoundException NotResolved() =>
        new(
            "location_not_resolved",
            "No usable address was found for the selected location.");

    private sealed record CityCandidate(Guid Id, string Name);

    private sealed record NominatimResult(
        [property: JsonPropertyName("osm_type")] string? OsmType,
        [property: JsonPropertyName("osm_id")] long? OsmId,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("lat")] string Latitude,
        [property: JsonPropertyName("lon")] string Longitude,
        [property: JsonPropertyName("address")] NominatimAddress? Address);

    private sealed record NominatimAddress(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("town")] string? Town,
        [property: JsonPropertyName("municipality")] string? Municipality,
        [property: JsonPropertyName("village")] string? Village,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("state_district")] string? StateDistrict,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("country_code")] string? CountryCode);
}
