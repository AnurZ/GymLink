using GymLink.Domain.Common;

namespace GymLink.Domain.Engagement;

public sealed class Conversation : TenantEntity, IConcurrencyTracked
{
    public Guid? ReservationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime? ClosedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ConversationParticipant : TenantEntity
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public DateTime? LeftAtUtc { get; set; }
}

public sealed class Message : TenantEntity
{
    public Guid ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public DateTime? EditedAtUtc { get; set; }
}

public sealed class Notification : AuditedEntity, IConcurrencyTracked
{
    public Guid RecipientUserId { get; set; }
    public Guid? TenantId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime? ReadAtUtc { get; set; }
    public string? CorrelationId { get; set; }
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public Guid? SourceMessageId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public void MarkRead(DateTime readAtUtc)
    {
        EnsureUtc(readAtUtc, nameof(readAtUtc));
        ReadAtUtc ??= readAtUtc;
    }

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
