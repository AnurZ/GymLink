using GymLink.Domain.Common;

namespace GymLink.Domain.Identity;

public sealed class UserProfile : AuditedEntity, IConcurrencyTracked
{
    private UserProfile() { }

    public UserProfile(Guid id, string displayName)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("invalid_user_id", "User ID is required.");
        }

        Id = id;
        DisplayName = displayName;
    }

    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ImageStorageKey { get; set; }
    public int TokenVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class RefreshTokenSession : AuditedEntity, IConcurrencyTracked
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Jti { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
    public string? RevocationReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SecurityAuditRecord : Entity
{
    public Guid ActorUserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public Guid? TargetTenantId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string? Reason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
