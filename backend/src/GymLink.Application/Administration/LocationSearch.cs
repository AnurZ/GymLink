using System.ComponentModel.DataAnnotations;

namespace GymLink.Application.Administration;

public sealed record LocationSearchRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public required string Query { get; init; }
}

public sealed record LocationSearchResultDto(
    string ResultKey,
    string DisplayName,
    string Address,
    Guid CityId,
    string CityName,
    decimal Latitude,
    decimal Longitude);

public interface ILocationSearchService
{
    Task<IReadOnlyList<LocationSearchResultDto>> SearchAsync(
        LocationSearchRequest request,
        CancellationToken cancellationToken);
}
