using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Reservations;

public sealed class AppointmentReservation : TenantEntity, IConcurrencyTracked
{
    private AppointmentReservation() { }

    public AppointmentReservation(
        Guid tenantId,
        Guid memberUserId,
        Guid trainerProfileId,
        Guid trainerServiceOfferingId,
        DateTime startsAtUtc,
        int durationMinutes,
        decimal price,
        string currency)
    {
        if (startsAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Reservation time must use UTC.");
        }

        if (durationMinutes <= 0 || price < 0)
        {
            throw new DomainException("invalid_booking_quote", "Duration and price must be valid.");
        }

        TenantId = tenantId;
        MemberUserId = memberUserId;
        TrainerProfileId = trainerProfileId;
        TrainerServiceOfferingId = trainerServiceOfferingId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = startsAtUtc.AddMinutes(durationMinutes);
        DurationMinutes = durationMinutes;
        Price = price;
        Currency = currency;
    }

    public Guid MemberUserId { get; set; }
    public Guid TrainerProfileId { get; set; }
    public Guid TrainerServiceOfferingId { get; set; }
    public Guid? AvailabilitySlotId { get; set; }
    public Guid? MembershipId { get; set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public Guid? PaymentId { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Review : TenantEntity
{
    public Guid ReservationId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public Guid TrainerProfileId { get; set; }
    public int Rating { get; private set; }
    public string? Comment { get; set; }

    public void SetRating(int rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new DomainException("invalid_rating", "Rating must be between 1 and 5.");
        }

        Rating = rating;
    }
}
