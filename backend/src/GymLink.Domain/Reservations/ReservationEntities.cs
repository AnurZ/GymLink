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
        Guid? availabilitySlotId,
        Guid membershipId,
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
        AvailabilitySlotId = availabilitySlotId;
        MembershipId = membershipId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = startsAtUtc.AddMinutes(durationMinutes);
        DurationMinutes = durationMinutes;
        Price = price;
        Currency = currency;
    }

    public Guid MemberUserId { get; set; }
    public Guid TrainerProfileId { get; set; }
    public Guid TrainerServiceOfferingId { get; set; }
    public Guid? AvailabilitySlotId { get; private set; }
    public Guid MembershipId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public ReservationStatus Status { get; private set; } = ReservationStatus.Pending;
    public Guid? PaymentId { get; private set; }
    public DateTime? PaymentDueAtUtc { get; private set; }
    public Guid? ConfirmedByUserId { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void Confirm(Guid actorUserId, DateTime occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc);
        EnsureState(ReservationStatus.Pending);
        if (PaymentDueAtUtc.HasValue)
        {
            throw new DomainException(
                "payment_confirmation_required",
                "A prepaid reservation can only be confirmed by the payment provider.");
        }

        Status = ReservationStatus.Confirmed;
        ConfirmedByUserId = actorUserId;
        ConfirmedAtUtc = occurredAtUtc;
    }

    public void RequirePayment(DateTime paymentDueAtUtc)
    {
        EnsureUtc(paymentDueAtUtc);
        EnsureState(ReservationStatus.Pending);
        if (paymentDueAtUtc >= StartsAtUtc)
        {
            throw new DomainException(
                "invalid_payment_deadline",
                "The payment deadline must be before the appointment starts.");
        }

        PaymentDueAtUtc = paymentDueAtUtc;
    }

    public void ConfirmFromPayment(Guid paymentId, DateTime occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc);
        EnsureState(ReservationStatus.Pending);
        if (paymentId == Guid.Empty)
        {
            throw new DomainException("payment_required", "A verified payment is required.");
        }

        if (!PaymentDueAtUtc.HasValue)
        {
            throw new DomainException(
                "payment_not_required",
                "This reservation does not require online payment.");
        }

        PaymentId = paymentId;
        Status = ReservationStatus.Confirmed;
        ConfirmedByUserId = null;
        ConfirmedAtUtc = occurredAtUtc;
    }

    public void ExpireUnpaid(DateTime occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc);
        EnsureState(ReservationStatus.Pending);
        if (!PaymentDueAtUtc.HasValue || occurredAtUtc < PaymentDueAtUtc)
        {
            throw new DomainException(
                "payment_window_open",
                "The reservation payment window has not expired.");
        }

        Status = ReservationStatus.Cancelled;
        CancelledByUserId = null;
        CancelledAtUtc = occurredAtUtc;
        CancellationReason = "Payment window expired.";
    }

    public void Complete(Guid actorUserId, DateTime occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc);
        EnsureState(ReservationStatus.Confirmed);
        Status = ReservationStatus.Completed;
        CompletedByUserId = actorUserId;
        CompletedAtUtc = occurredAtUtc;
    }

    public void CancelByMember(Guid memberUserId, DateTime occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc);
        EnsureCancellable();
        if (memberUserId != MemberUserId)
        {
            throw new DomainException(
                "reservation_owner_required",
                "Only the owning Member can cancel this reservation.");
        }

        if (occurredAtUtc >= StartsAtUtc)
        {
            throw new DomainException(
                "cancellation_window_closed",
                "Member cancellation is allowed only before the appointment starts.");
        }

        Cancel(memberUserId, occurredAtUtc, null);
    }

    public void CancelByStaff(Guid actorUserId, DateTime occurredAtUtc, string reason)
    {
        EnsureUtc(occurredAtUtc);
        EnsureCancellable();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "cancellation_reason_required",
                "A cancellation reason is required.");
        }

        Cancel(actorUserId, occurredAtUtc, reason.Trim());
    }

    private void Cancel(Guid actorUserId, DateTime occurredAtUtc, string? reason)
    {
        Status = ReservationStatus.Cancelled;
        CancelledByUserId = actorUserId;
        CancelledAtUtc = occurredAtUtc;
        CancellationReason = reason;
    }

    private void EnsureCancellable()
    {
        if (PaymentId.HasValue)
        {
            throw new DomainException(
                "paid_cancellation_not_supported",
                "Paid reservations cannot be cancelled because refunds are not supported.");
        }

        if (Status is not ReservationStatus.Pending and not ReservationStatus.Confirmed)
        {
            throw InvalidTransition();
        }
    }

    private void EnsureState(ReservationStatus expected)
    {
        if (Status != expected)
        {
            throw InvalidTransition();
        }
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Reservation times must use UTC.");
        }
    }

    private static DomainException InvalidTransition() =>
        new(
            "invalid_state_transition",
            "The reservation cannot perform that transition from its current state.");
}

public sealed class Review : TenantEntity
{
    private Review() { }

    public Review(
        Guid tenantId,
        Guid reservationId,
        Guid reviewerUserId,
        Guid trainerProfileId,
        int rating,
        string? comment)
    {
        TenantId = tenantId;
        ReservationId = reservationId;
        ReviewerUserId = reviewerUserId;
        TrainerProfileId = trainerProfileId;
        SetRating(rating);
        Comment = NormalizeComment(comment);
    }

    public Guid ReservationId { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public Guid TrainerProfileId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }

    private void SetRating(int rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new DomainException("invalid_rating", "Rating must be between 1 and 5.");
        }

        Rating = rating;
    }

    private static string? NormalizeComment(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
}

public sealed class GymReview : TenantEntity
{
    private GymReview() { }

    public GymReview(
        Guid tenantId,
        Guid gymId,
        Guid reviewerUserId,
        int rating,
        string? comment)
    {
        if (rating is < 1 or > 5)
        {
            throw new DomainException("invalid_rating", "Rating must be between 1 and 5.");
        }

        TenantId = tenantId;
        GymId = gymId;
        ReviewerUserId = reviewerUserId;
        Rating = rating;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    public Guid GymId { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }
}
