using GymLink.Domain.Common;

namespace GymLink.Domain.Identity;

public sealed class ApplicationUser : AuditedEntity, IConcurrencyTracked
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ImageStorageKey { get; set; }
    public int TokenVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class RefreshTokenSession : AuditedEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Jti { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
}
