using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Tenancy;

public sealed class Tenant : AuditedEntity, IConcurrencyTracked
{
    private Tenant() { }

    public Tenant(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("invalid_tenant_id", "Tenant ID is required.");
        }

        Id = id;
        Name = name;
    }

    public string Name { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.PendingActivation;
    public Guid? StatusChangedByUserId { get; set; }
    public DateTime? StatusChangedAtUtc { get; set; }
    public string? StatusReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GymRegistrationRequest : AuditedEntity, IConcurrencyTracked
{
    public Guid ApplicantUserId { get; set; }
    public string ProposedGymName { get; set; } = string.Empty;
    public string ProposedAddress { get; set; } = string.Empty;
    public Guid CityId { get; set; }
    public GymRegistrationStatus Status { get; set; } = GymRegistrationStatus.Draft;
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? CreatedTenantId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class UserGymAssignment : TenantEntity, IConcurrencyTracked
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Invited;
    public DateTime StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string? Reason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
