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

public sealed class PasswordResetChallenge : Entity, IConcurrencyTracked
{
    private const int MaximumFailedAttempts = 5;

    private PasswordResetChallenge() { }

    public PasswordResetChallenge(
        Guid id,
        Guid userId,
        string codeHash,
        string codeSalt,
        DateTime requestedAtUtc,
        DateTime expiresAtUtc,
        string? requestIpHash,
        string correlationId)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException("invalid_user_id", "User ID is required.");
        }

        if (string.IsNullOrWhiteSpace(codeHash) || string.IsNullOrWhiteSpace(codeSalt))
        {
            throw new DomainException(
                "invalid_password_reset_code",
                "Password reset code material is required.");
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new DomainException(
                "invalid_correlation_id",
                "Correlation ID is required.");
        }

        EnsureUtc(requestedAtUtc, nameof(requestedAtUtc));
        EnsureUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtUtc <= requestedAtUtc)
        {
            throw new DomainException(
                "invalid_password_reset_expiry",
                "Password reset expiry must follow its request time.");
        }

        Id = id;
        UserId = userId;
        CodeHash = codeHash;
        CodeSalt = codeSalt;
        RequestedAtUtc = requestedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RequestIpHash = string.IsNullOrWhiteSpace(requestIpHash)
            ? null
            : requestIpHash.Trim();
        CorrelationId = correlationId.Trim();
    }

    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public string CodeSalt { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? LastFailedAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? SupersededAtUtc { get; private set; }
    public string? RequestIpHash { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public byte[] RowVersion { get; set; } = [];

    public bool CanConfirm(DateTime nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        return ConsumedAtUtc is null &&
            SupersededAtUtc is null &&
            FailedAttempts < MaximumFailedAttempts &&
            nowUtc < ExpiresAtUtc;
    }

    public void RegisterFailedAttempt(DateTime occurredAtUtc)
    {
        EnsureUsable(occurredAtUtc);
        FailedAttempts++;
        LastFailedAtUtc = occurredAtUtc;
    }

    public void Consume(DateTime occurredAtUtc)
    {
        EnsureUsable(occurredAtUtc);
        ConsumedAtUtc = occurredAtUtc;
    }

    public void Supersede(DateTime occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (ConsumedAtUtc is not null || SupersededAtUtc is not null)
        {
            throw InvalidChallenge();
        }

        SupersededAtUtc = occurredAtUtc;
    }

    private void EnsureUsable(DateTime occurredAtUtc)
    {
        if (!CanConfirm(occurredAtUtc))
        {
            throw InvalidChallenge();
        }
    }

    private static DomainException InvalidChallenge() =>
        new(
            "password_reset_invalid",
            "The password reset code is invalid or expired.");

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "timestamp_must_be_utc",
                $"{parameterName} must be UTC.");
        }
    }
}
