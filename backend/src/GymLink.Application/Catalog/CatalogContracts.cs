using System.ComponentModel.DataAnnotations;
using GymLink.Application.Common;

namespace GymLink.Application.Catalog;

public sealed record GymSearchRequest : PagedRequest
{
    [MaxLength(200)]
    public string? Query { get; init; }

    public Guid? CityId { get; init; }
}

public sealed record GymListItemDto(
    Guid Id,
    string Name,
    string Address,
    string City,
    string? PrimaryImageUrl,
    decimal? StartingMembershipPrice,
    string? Currency);

public sealed record GymDetailsDto(
    Guid Id,
    string Name,
    string Description,
    string Address,
    Guid CityId,
    string City,
    decimal Latitude,
    decimal Longitude,
    string? PhoneNumber,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> TrainingTypes,
    IReadOnlyList<string> ImageUrls);

public sealed record WorkingHoursRequest
{
    [Range(0, 6)]
    public int DayOfWeek { get; init; }

    public TimeOnly? OpensAt { get; init; }
    public TimeOnly? ClosesAt { get; init; }
    public bool IsClosed { get; init; }
}

public sealed record UpdateGymRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, MaxLength(4000)]
    public required string Description { get; init; }

    [Required, MaxLength(300)]
    public required string Address { get; init; }

    public Guid CityId { get; init; }

    [Range(-90, 90)]
    public decimal Latitude { get; init; }

    [Range(-180, 180)]
    public decimal Longitude { get; init; }

    [MaxLength(32)]
    public string? PhoneNumber { get; init; }

    public IReadOnlyList<Guid> EquipmentIds { get; init; } = [];
    public IReadOnlyList<Guid> TrainingTypeIds { get; init; } = [];
    public IReadOnlyList<WorkingHoursRequest> WorkingHours { get; init; } = [];
}

public sealed record TrainerSearchRequest : PagedRequest
{
    [MaxLength(160)]
    public string? Query { get; init; }

    public bool? IsActive { get; init; }
}

public sealed record TrainerDto(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string Biography,
    string? Credentials,
    bool IsActive,
    decimal AverageRating,
    int ReviewCount,
    IReadOnlyList<Guid> TrainingTypeIds);

public record CreateTrainerRequest
{
    public Guid UserId { get; init; }

    [Required, MaxLength(4000)]
    public required string Biography { get; init; }

    [MaxLength(2000)]
    public string? Credentials { get; init; }

    public IReadOnlyList<Guid> TrainingTypeIds { get; init; } = [];
}

public sealed record UpdateTrainerRequest : CreateTrainerRequest
{
    public bool IsActive { get; init; }
}

public sealed record CatalogSearchRequest : PagedRequest
{
    [MaxLength(160)]
    public string? Query { get; init; }

    public bool? IsActive { get; init; }
}

public sealed record MembershipPlanDto(
    Guid Id,
    Guid GymId,
    string Name,
    int DurationDays,
    decimal Price,
    string Currency,
    bool IsActive);

public record CreateMembershipPlanRequest
{
    [Required, MaxLength(160)]
    public required string Name { get; init; }

    [Range(1, 3660)]
    public int DurationDays { get; init; }

    [Range(0, 1000000)]
    public decimal Price { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public required string Currency { get; init; }
}

public sealed record UpdateMembershipPlanRequest : CreateMembershipPlanRequest
{
    public bool IsActive { get; init; }
}

public sealed record TrainerOfferingDto(
    Guid Id,
    Guid TrainerProfileId,
    Guid TrainingTypeId,
    string TrainingType,
    string Name,
    int DurationMinutes,
    decimal Price,
    string Currency,
    bool IsActive);

public record CreateTrainerOfferingRequest
{
    public Guid TrainerProfileId { get; init; }
    public Guid TrainingTypeId { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Range(1, 1440)]
    public int DurationMinutes { get; init; }

    [Range(0, 1000000)]
    public decimal Price { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public required string Currency { get; init; }
}

public sealed record UpdateTrainerOfferingRequest : CreateTrainerOfferingRequest
{
    public bool IsActive { get; init; }
}
