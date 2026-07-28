using System.ComponentModel.DataAnnotations;
using GymLink.Application.Common;
using GymLink.Domain.Enums;

namespace GymLink.Application.Reservations;

public sealed record AvailabilitySearchRequest : PagedRequest
{
    public Guid? TrainerProfileId { get; init; }
    public AvailabilitySlotStatus? Status { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

public sealed record PublicAvailabilitySearchRequest : PagedRequest
{
    public Guid? TrainerServiceOfferingId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

public sealed record AvailabilityDto(
    Guid Id,
    Guid TrainerProfileId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    AvailabilitySlotStatus Status,
    string ConcurrencyToken);

public record CreateAvailabilityRequest
{
    public Guid TrainerProfileId { get; init; }
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public AvailabilitySlotStatus Status { get; init; } = AvailabilitySlotStatus.Available;
}

public sealed record UpdateAvailabilityRequest : CreateAvailabilityRequest
{
    [Required]
    public required string ConcurrencyToken { get; init; }
}

public sealed record ReservationSearchRequest : PagedRequest
{
    public Guid? TrainerProfileId { get; init; }
    public ReservationStatus? Status { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

public sealed record CreateReservationRequest
{
    public Guid TrainerServiceOfferingId { get; init; }
    public Guid AvailabilitySlotId { get; init; }
}

public record ReservationConcurrencyRequest
{
    [Required]
    public required string ConcurrencyToken { get; init; }
}

public sealed record StaffCancellationRequest : ReservationConcurrencyRequest
{
    [Required, MaxLength(1000)]
    public required string Reason { get; init; }
}

public sealed record ReservationDto(
    Guid Id,
    Guid TrainerProfileId,
    string TrainerName,
    string MemberName,
    string GymName,
    string OfferingName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int DurationMinutes,
    decimal Price,
    string Currency,
    ReservationStatus Status,
    string? CancellationReason,
    bool CanReview,
    string ConcurrencyToken);

public sealed record CreateReviewRequest
{
    [Range(1, 5)]
    public int Rating { get; init; }

    [MaxLength(2000)]
    public string? Comment { get; init; }
}

public sealed record ReviewSearchRequest : PagedRequest;

public sealed record ReviewDto(
    Guid Id,
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc);

public interface IAvailabilityService
{
    Task<PagedResult<AvailabilityDto>> SearchTenantAsync(AvailabilitySearchRequest request, CancellationToken cancellationToken);
    Task<PagedResult<AvailabilityDto>> SearchPublicAsync(Guid trainerId, PublicAvailabilitySearchRequest request, CancellationToken cancellationToken);
    Task<AvailabilityDto> CreateAsync(CreateAvailabilityRequest request, CancellationToken cancellationToken);
    Task<AvailabilityDto> UpdateAsync(Guid id, UpdateAvailabilityRequest request, CancellationToken cancellationToken);
    Task<AvailabilityDto> CancelAsync(Guid id, ReservationConcurrencyRequest request, CancellationToken cancellationToken);
}

public interface IReservationService
{
    Task<ReservationDto> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ReservationDto>> SearchMineAsync(ReservationSearchRequest request, CancellationToken cancellationToken);
    Task<ReservationDto> GetMineAsync(Guid id, CancellationToken cancellationToken);
    Task<ReservationDto> CancelMineAsync(Guid id, ReservationConcurrencyRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ReservationDto>> SearchTrainerAsync(ReservationSearchRequest request, CancellationToken cancellationToken);
    Task<ReservationDto> GetTrainerAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<ReservationDto>> SearchTenantAsync(ReservationSearchRequest request, CancellationToken cancellationToken);
    Task<ReservationDto> GetTenantAsync(Guid id, CancellationToken cancellationToken);
    Task<ReservationDto> ConfirmAsync(Guid id, ReservationConcurrencyRequest request, CancellationToken cancellationToken);
    Task<ReservationDto> CancelStaffAsync(Guid id, StaffCancellationRequest request, CancellationToken cancellationToken);
    Task<ReservationDto> CompleteAsync(Guid id, ReservationConcurrencyRequest request, CancellationToken cancellationToken);
}

public interface IReviewService
{
    Task<ReviewDto> CreateTrainerReviewAsync(Guid reservationId, CreateReviewRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ReviewDto>> SearchTrainerReviewsAsync(Guid trainerId, ReviewSearchRequest request, CancellationToken cancellationToken);
    Task<ReviewDto> CreateGymReviewAsync(Guid gymId, CreateReviewRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ReviewDto>> SearchGymReviewsAsync(Guid gymId, ReviewSearchRequest request, CancellationToken cancellationToken);
}

public sealed record ReservationWorkflowEventIntent(
    string Name,
    Guid TenantId,
    Guid ActorUserId,
    Guid TargetId,
    DateTime OccurredAtUtc);

public interface IReservationWorkflowEventRecorder
{
    void Record(ReservationWorkflowEventIntent intent);
}
