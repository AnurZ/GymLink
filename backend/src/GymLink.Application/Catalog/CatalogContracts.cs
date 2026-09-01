using System.ComponentModel.DataAnnotations;
using GymLink.Application.Common;
using GymLink.Application.TrainerImages;
using GymLink.Application.GymImages;

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
    decimal Latitude,
    decimal Longitude,
    string? PrimaryImageUrl,
    decimal? StartingMembershipPrice,
    string? Currency,
    decimal AverageRating,
    int ReviewCount);

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
    decimal AverageRating,
    int ReviewCount,
    IReadOnlyList<Guid> EquipmentIds,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<Guid> TrainingTypeIds,
    IReadOnlyList<string> TrainingTypes,
    IReadOnlyList<WorkingHoursDto> WorkingHours,
    IReadOnlyList<string> ImageUrls,
    GymImageGalleryDto? ImageGallery);

public sealed record WorkingHoursDto(
    int DayOfWeek,
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt,
    bool IsClosed);

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
    IReadOnlyList<Guid> TrainingTypeIds,
    string? ImageUrl,
    TrainerImageDto? ManagementImage);

public sealed record TrainerCandidateSearchRequest : PagedRequest
{
    [MaxLength(160)]
    public string? Query { get; init; }
}

public sealed record TrainerCandidateDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string MembershipPlan,
    DateTime MembershipEndsAtUtc);

public record TrainerWriteRequest
{
    [Required, MaxLength(4000)]
    public required string Biography { get; init; }

    [MaxLength(2000)]
    public string? Credentials { get; init; }

    public IReadOnlyList<Guid> TrainingTypeIds { get; init; } = [];
}

public sealed record CreateTrainerRequest : TrainerWriteRequest
{
    public Guid UserId { get; init; }

    [Required, StringLength(200, MinimumLength = 2)]
    public required string Reason { get; init; }
}

public sealed record UpdateTrainerRequest : TrainerWriteRequest
{
    public Guid UserId { get; init; }
}

public sealed record TrainerLifecycleRequest : IValidatableObject
{
    [Required]
    public required string Reason { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var length = Reason?.Trim().Length ?? 0;
        if (length is < 2 or > 200)
        {
            yield return new ValidationResult(
                "Reason must contain between 2 and 200 characters.",
                [nameof(Reason)]);
        }
    }
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
