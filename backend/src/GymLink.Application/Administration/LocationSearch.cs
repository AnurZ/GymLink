using System.ComponentModel.DataAnnotations;

namespace GymLink.Application.Administration;

public sealed record LocationSearchRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public required string Query { get; init; }
}

public sealed record LocationReverseRequest
{
    [Required, Range(typeof(decimal), "-90", "90")]
    public decimal? Latitude { get; init; }

    [Required, Range(typeof(decimal), "-180", "180")]
    public decimal? Longitude { get; init; }
}

public sealed record LocationSearchResultDto(
    string ResultKey,
    string DisplayName,
    string Address,
    Guid CityId,
    string CityName,
    decimal Latitude,
    decimal Longitude);

public sealed record LocationReverseResultDto(
    string ResultKey,
    string DisplayName,
    string Address,
    Guid CityId,
    string CityName);

public interface ILocationSearchService
{
    Task<IReadOnlyList<LocationSearchResultDto>> SearchAsync(
        LocationSearchRequest request,
        CancellationToken cancellationToken);

    Task<LocationReverseResultDto> ReverseAsync(
        LocationReverseRequest request,
        CancellationToken cancellationToken);
}
