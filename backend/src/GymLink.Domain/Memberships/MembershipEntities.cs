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
    public MembershipRequestStatus Status { get; set; } = MembershipRequestStatus.Pending;
    public DateTime RequestedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Membership : TenantEntity, IConcurrencyTracked
{
    public Guid MemberUserId { get; set; }
    public Guid GymId { get; set; }
    public Guid MembershipPlanId { get; set; }
    public Guid MembershipRequestId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.PendingPayment;
    public Guid? PaymentId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
