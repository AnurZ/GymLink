using System.ComponentModel.DataAnnotations;
using GymLink.Application.Common;

namespace GymLink.Application.ReferenceData;

public sealed record ReferenceSearchRequest : PagedRequest
{
    [MaxLength(160)]
    public string? Query { get; init; }

    public bool? IsActive { get; init; }
}

public sealed record CitySearchRequest : PagedRequest
{
    [MaxLength(160)]
    public string? Query { get; init; }

    public Guid? CountryId { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record CountryDto(Guid Id, string Code, string Name, bool IsActive);
public sealed record CityDto(
    Guid Id,
    Guid CountryId,
    string CountryName,
    string Name,
    bool IsActive);
public sealed record EquipmentDto(Guid Id, string Name, bool IsActive);
public sealed record TrainingTypeDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive);

public record CreateCountryRequest
{
    [Required, StringLength(3, MinimumLength = 2)]
    public required string Code { get; init; }

    [Required, MaxLength(120)]
    public required string Name { get; init; }
}

public sealed record UpdateCountryRequest : CreateCountryRequest
{
    public bool IsActive { get; init; }
}

public record CreateCityRequest
{
    public Guid CountryId { get; init; }

    [Required, MaxLength(160)]
    public required string Name { get; init; }
}

public sealed record UpdateCityRequest : CreateCityRequest
{
    public bool IsActive { get; init; }
}

public record CreateEquipmentRequest
{
    [Required, MaxLength(160)]
    public required string Name { get; init; }
}

public sealed record UpdateEquipmentRequest : CreateEquipmentRequest
{
    public bool IsActive { get; init; }
}

public record CreateTrainingTypeRequest
{
    [Required, MaxLength(160)]
    public required string Name { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }
}

public sealed record UpdateTrainingTypeRequest : CreateTrainingTypeRequest
{
    public bool IsActive { get; init; }
}

public sealed record ReferenceLookupsDto(
    IReadOnlyList<CountryDto> Countries,
    IReadOnlyList<CityDto> Cities,
    IReadOnlyList<EquipmentDto> Equipment,
    IReadOnlyList<TrainingTypeDto> TrainingTypes);
