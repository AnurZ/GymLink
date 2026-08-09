using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Memberships;

public sealed class MembershipPlan : TenantEntity, IConcurrencyTracked
{
    public Guid GymId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class MembershipRequest : TenantEntity, IConcurrencyTracked
{
    public Guid MemberUserId { get; set; }
    public Guid GymId { get; set; }
    public Guid MembershipPlanId { get; set; }
    public MembershipPaymentMethod PaymentMethod { get; set; } = MembershipPaymentMethod.Stripe;
    public MembershipRequestStatus Status { get; private set; } = MembershipRequestStatus.Pending;
    public DateTime RequestedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void Approve(Guid actorUserId, DateTime decidedAtUtc)
    {
        EnsurePending();
        EnsureActorAndUtc(actorUserId, decidedAtUtc);
        Status = MembershipRequestStatus.Approved;
        DecidedByUserId = actorUserId;
        DecidedAtUtc = decidedAtUtc;
        DecisionReason = null;
    }

    public void Reject(Guid actorUserId, DateTime decidedAtUtc, string reason)
    {
        EnsurePending();
        EnsureActorAndUtc(actorUserId, decidedAtUtc);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("reason_required", "A rejection reason is required.");
        }

        Status = MembershipRequestStatus.Rejected;
        DecidedByUserId = actorUserId;
        DecidedAtUtc = decidedAtUtc;
        DecisionReason = reason.Trim();
    }

    public void Cancel(Guid actorUserId, DateTime cancelledAtUtc)
    {
        EnsurePending();
        EnsureActorAndUtc(actorUserId, cancelledAtUtc);
        Status = MembershipRequestStatus.Cancelled;
        DecidedByUserId = actorUserId;
        DecidedAtUtc = cancelledAtUtc;
        DecisionReason = null;
    }

    private void EnsurePending()
    {
        if (Status != MembershipRequestStatus.Pending)
        {
            throw new DomainException(
                "invalid_state_transition",
                $"Membership request cannot transition from {Status}.");
        }
    }

    private static void EnsureActorAndUtc(Guid actorUserId, DateTime occurredAtUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new DomainException("actor_required", "A status-change actor is required.");
        }

        EnsureUtc(occurredAtUtc);
    }

    private static void EnsureUtc(DateTime occurredAtUtc)
    {
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Status-change time must use UTC.");
        }
    }
}

public sealed class Membership : TenantEntity, IConcurrencyTracked
{
    private Membership() { }

    private Membership(
        Guid tenantId,
        Guid memberUserId,
        Guid gymId,
        Guid membershipPlanId,
        Guid membershipRequestId,
        string planName,
        int durationDays,
        decimal price,
        string currency)
    {
        if (durationDays <= 0)
        {
            throw new DomainException(
                "invalid_duration",
                "Membership duration must be greater than zero.");
        }

        if (price < 0)
        {
            throw new DomainException("invalid_price", "Membership price cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(planName) || string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException(
                "invalid_membership_snapshot",
                "Membership plan name and currency are required.");
        }

        TenantId = tenantId;
        MemberUserId = memberUserId;
        GymId = gymId;
        MembershipPlanId = membershipPlanId;
        MembershipRequestId = membershipRequestId;
        PlanName = planName.Trim();
        DurationDays = durationDays;
        Price = price;
        Currency = currency.Trim().ToUpperInvariant();
    }

    public Membership(
        Guid tenantId,
        Guid memberUserId,
        Guid gymId,
        Guid membershipPlanId,
        Guid membershipRequestId,
        string planName,
        int durationDays,
        decimal price,
        string currency,
        Guid activatedByUserId,
        DateTime activatedAtUtc)
        : this(
            tenantId,
            memberUserId,
            gymId,
            membershipPlanId,
            membershipRequestId,
            planName,
            durationDays,
            price,
            currency)
    {
        EnsureActorAndUtc(activatedByUserId, activatedAtUtc);
        StartsAtUtc = activatedAtUtc;
        EndsAtUtc = activatedAtUtc.AddDays(durationDays);
        Status = MembershipStatus.Active;
        StatusChangedByUserId = activatedByUserId;
        StatusChangedAtUtc = activatedAtUtc;
    }

    public static Membership CreatePendingPayment(
        Guid tenantId,
        Guid memberUserId,
        Guid gymId,
        Guid membershipPlanId,
        Guid membershipRequestId,
        string planName,
        int durationDays,
        decimal price,
        string currency,
        Guid approvedByUserId,
        DateTime approvedAtUtc)
    {
        EnsureActorAndUtc(approvedByUserId, approvedAtUtc);
        return new Membership(
            tenantId,
            memberUserId,
            gymId,
            membershipPlanId,
            membershipRequestId,
            planName,
            durationDays,
            price,
            currency)
        {
            Status = MembershipStatus.PendingPayment,
            StatusChangedByUserId = approvedByUserId,
            StatusChangedAtUtc = approvedAtUtc,
        };
    }

    public Guid MemberUserId { get; private set; }
    public Guid GymId { get; private set; }
    public Guid MembershipPlanId { get; private set; }
    public Guid MembershipRequestId { get; private set; }
    public string PlanName { get; private set; } = string.Empty;
    public int DurationDays { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTime? StartsAtUtc { get; private set; }
    public DateTime? EndsAtUtc { get; private set; }
    public MembershipStatus Status { get; private set; } = MembershipStatus.PendingPayment;
    public Guid? PaymentId { get; private set; }
    public Guid? StatusChangedByUserId { get; private set; }
    public DateTime? StatusChangedAtUtc { get; private set; }
    public string? StatusReason { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void ActivateFromPayment(Guid paymentId, DateTime activatedAtUtc)
    {
        EnsurePaymentIdAndUtc(paymentId, activatedAtUtc);
        EnsureStatus(MembershipStatus.PendingPayment, MembershipStatus.Active);
        PaymentId = paymentId;
        StartsAtUtc = activatedAtUtc;
        EndsAtUtc = activatedAtUtc.AddDays(DurationDays);
        Status = MembershipStatus.Active;
        StatusChangedByUserId = null;
        StatusChangedAtUtc = activatedAtUtc;
        StatusReason = null;
    }

    public void CancelPendingPayment(Guid actorUserId, DateTime cancelledAtUtc)
    {
        EnsureStatus(MembershipStatus.PendingPayment, MembershipStatus.Cancelled);
        SetStatus(MembershipStatus.Cancelled, actorUserId, cancelledAtUtc, null);
    }

    public void CancelByMember(Guid actorUserId, DateTime cancelledAtUtc)
    {
        EnsurePaymentlessCancellation();
        EnsureStatus(MembershipStatus.Active, MembershipStatus.Cancelled);
        SetStatus(MembershipStatus.Cancelled, actorUserId, cancelledAtUtc, null);
    }

    public void CancelByStaff(Guid actorUserId, DateTime cancelledAtUtc, string reason)
    {
        EnsurePaymentlessCancellation();
        EnsureStatus(MembershipStatus.Active, MembershipStatus.Cancelled);
        SetStatus(
            MembershipStatus.Cancelled,
            actorUserId,
            cancelledAtUtc,
            RequireReason(reason, "A cancellation reason is required."));
    }

    public void Suspend(Guid actorUserId, DateTime suspendedAtUtc, string reason)
    {
        EnsureStatus(MembershipStatus.Active, MembershipStatus.Suspended);
        SetStatus(
            MembershipStatus.Suspended,
            actorUserId,
            suspendedAtUtc,
            RequireReason(reason, "A suspension reason is required."));
    }

    public void Reactivate(Guid actorUserId, DateTime reactivatedAtUtc, string reason)
    {
        EnsureStatus(MembershipStatus.Suspended, MembershipStatus.Active);
        if (!EndsAtUtc.HasValue || EndsAtUtc <= reactivatedAtUtc)
        {
            throw new DomainException(
                "membership_expired",
                "An expired membership cannot be reactivated.");
        }

        SetStatus(
            MembershipStatus.Active,
            actorUserId,
            reactivatedAtUtc,
            RequireReason(reason, "A reactivation reason is required."));
    }

    public void Expire(Guid actorUserId, DateTime expiredAtUtc)
    {
        EnsureStatus(MembershipStatus.Active, MembershipStatus.Expired);
        if (!EndsAtUtc.HasValue || EndsAtUtc > expiredAtUtc)
        {
            throw new DomainException(
                "membership_not_expired",
                "The membership end date has not been reached.");
        }

        SetStatus(MembershipStatus.Expired, actorUserId, expiredAtUtc, null);
    }

    private void EnsurePaymentlessCancellation()
    {
        if (PaymentId.HasValue)
        {
            throw new DomainException(
                "paid_cancellation_not_supported",
                "Paid memberships cannot be cancelled because refunds are not supported.");
        }
    }

    private void EnsureStatus(MembershipStatus expected, MembershipStatus target)
    {
        if (Status != expected)
        {
            throw new DomainException(
                "invalid_state_transition",
                $"Membership cannot transition from {Status} to {target}.");
        }
    }

    private void SetStatus(
        MembershipStatus status,
        Guid actorUserId,
        DateTime changedAtUtc,
        string? reason)
    {
        EnsureActorAndUtc(actorUserId, changedAtUtc);
        Status = status;
        StatusChangedByUserId = actorUserId;
        StatusChangedAtUtc = changedAtUtc;
        StatusReason = reason;
    }

    private static string RequireReason(string reason, string message)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("reason_required", message);
        }

        return reason.Trim();
    }

    private static void EnsureActorAndUtc(Guid actorUserId, DateTime occurredAtUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new DomainException("actor_required", "A status-change actor is required.");
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Status-change time must use UTC.");
        }
    }

    private static void EnsurePaymentIdAndUtc(Guid paymentId, DateTime occurredAtUtc)
    {
        if (paymentId == Guid.Empty)
        {
            throw new DomainException("payment_required", "A verified payment is required.");
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Status-change time must use UTC.");
        }
    }
}
