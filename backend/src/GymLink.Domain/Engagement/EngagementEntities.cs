using GymLink.Domain.Common;

namespace GymLink.Domain.Engagement;

public sealed class Conversation : TenantEntity, IConcurrencyTracked
{
    public const string MemberTrainerType = "MemberTrainer";

    private Conversation() { }

    public Conversation(
        Guid tenantId,
        Guid reservationId,
        Guid memberUserId,
        Guid trainerUserId,
        DateTime createdAtUtc)
    {
        EnsureRequired(tenantId, nameof(tenantId));
        EnsureRequired(reservationId, nameof(reservationId));
        EnsureRequired(memberUserId, nameof(memberUserId));
        EnsureRequired(trainerUserId, nameof(trainerUserId));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        if (memberUserId == trainerUserId)
        {
            throw new DomainException(
                "conversation_participants_invalid",
                "A conversation requires distinct Member and Trainer participants.");
        }

        TenantId = tenantId;
        ReservationId = reservationId;
        MemberUserId = memberUserId;
        TrainerUserId = trainerUserId;
        Type = MemberTrainerType;
        CreatedAtUtc = createdAtUtc;
        CreatedByUserId = memberUserId;
    }

    public Guid? ReservationId { get; private set; }
    public Guid MemberUserId { get; private set; }
    public Guid TrainerUserId { get; private set; }
    public string Type { get; private set; } = MemberTrainerType;
    public DateTime? LastMessageAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void RecordMessage(DateTime sentAtUtc)
    {
        EnsureOpen();
        EnsureUtc(sentAtUtc, nameof(sentAtUtc));
        if (LastMessageAtUtc.HasValue && sentAtUtc < LastMessageAtUtc.Value)
        {
            throw new DomainException(
                "message_timestamp_invalid",
                "A message cannot precede the conversation's latest message.");
        }

        LastMessageAtUtc = sentAtUtc;
    }

    public void Close(DateTime closedAtUtc)
    {
        EnsureUtc(closedAtUtc, nameof(closedAtUtc));
        ClosedAtUtc ??= closedAtUtc;
    }

    private void EnsureOpen()
    {
        if (ClosedAtUtc.HasValue)
        {
            throw new DomainException(
                "conversation_closed",
                "The conversation is read-only.");
        }
    }

    private static void EnsureRequired(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException(
                "conversation_identifier_required",
                $"{parameterName} is required.");
        }
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

public sealed class ConversationParticipant : TenantEntity
{
    private ConversationParticipant() { }

    public ConversationParticipant(
        Guid tenantId,
        Guid conversationId,
        Guid userId,
        DateTime joinedAtUtc)
    {
        EnsureRequired(tenantId, nameof(tenantId));
        EnsureRequired(conversationId, nameof(conversationId));
        EnsureRequired(userId, nameof(userId));
        EnsureUtc(joinedAtUtc, nameof(joinedAtUtc));

        TenantId = tenantId;
        ConversationId = conversationId;
        UserId = userId;
        JoinedAtUtc = joinedAtUtc;
        CreatedAtUtc = joinedAtUtc;
        CreatedByUserId = userId;
    }

    public Guid ConversationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime JoinedAtUtc { get; private set; }
    public DateTime? LastReadAtUtc { get; private set; }
    public DateTime? LeftAtUtc { get; private set; }

    public void MarkRead(DateTime readAtUtc)
    {
        EnsureActive();
        EnsureUtc(readAtUtc, nameof(readAtUtc));
        if (!LastReadAtUtc.HasValue || readAtUtc > LastReadAtUtc.Value)
        {
            LastReadAtUtc = readAtUtc;
        }
    }

    public void Leave(DateTime leftAtUtc)
    {
        EnsureUtc(leftAtUtc, nameof(leftAtUtc));
        LeftAtUtc ??= leftAtUtc;
    }

    private void EnsureActive()
    {
        if (LeftAtUtc.HasValue)
        {
            throw new DomainException(
                "conversation_participation_ended",
                "The conversation participation has ended.");
        }
    }

    private static void EnsureRequired(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException(
                "conversation_identifier_required",
                $"{parameterName} is required.");
        }
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

public sealed class Message : TenantEntity
{
    public const int MaximumTextLength = 2000;

    private Message() { }

    public Message(
        Guid tenantId,
        Guid conversationId,
        Guid senderUserId,
        Guid clientMessageId,
        string text,
        DateTime sentAtUtc)
    {
        EnsureRequired(tenantId, nameof(tenantId));
        EnsureRequired(conversationId, nameof(conversationId));
        EnsureRequired(senderUserId, nameof(senderUserId));
        EnsureRequired(clientMessageId, nameof(clientMessageId));
        EnsureUtc(sentAtUtc, nameof(sentAtUtc));
        var normalizedText = text?.Trim() ?? string.Empty;
        if (normalizedText.Length is 0 or > MaximumTextLength)
        {
            throw new DomainException(
                "message_text_invalid",
                $"Message text must contain between 1 and {MaximumTextLength} characters.");
        }

        TenantId = tenantId;
        ConversationId = conversationId;
        SenderUserId = senderUserId;
        ClientMessageId = clientMessageId;
        Text = normalizedText;
        SentAtUtc = sentAtUtc;
        CreatedAtUtc = sentAtUtc;
        CreatedByUserId = senderUserId;
    }

    public Guid ConversationId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public Guid ClientMessageId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTime SentAtUtc { get; private set; }
    public DateTime? EditedAtUtc { get; private set; }

    private static void EnsureRequired(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException(
                "conversation_identifier_required",
                $"{parameterName} is required.");
        }
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
