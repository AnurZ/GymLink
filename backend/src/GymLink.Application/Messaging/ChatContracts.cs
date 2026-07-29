using System.ComponentModel.DataAnnotations;
using GymLink.Application.Common;

namespace GymLink.Application.Messaging;

public sealed record OpenConversationRequest
{
    public Guid ReservationId { get; init; }
}

public sealed record ConversationSearchRequest : PagedRequest
{
    [MaxLength(100)]
    public string? Search { get; init; }
}

public sealed record MessageHistoryRequest
{
    public const int MaximumTake = 100;

    public DateTime? BeforeSentAtUtc { get; init; }
    public Guid? BeforeId { get; init; }
    public int Take { get; init; } = 50;

    public void Validate()
    {
        if (Take is < 1 or > MaximumTake)
        {
            throw new ValidationException(
                $"Take must be between 1 and {MaximumTake}.");
        }

        if (BeforeSentAtUtc.HasValue != BeforeId.HasValue)
        {
            throw new ValidationException(
                "Both cursor timestamp and message ID are required.");
        }

        if (BeforeSentAtUtc.HasValue &&
            BeforeSentAtUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new ValidationException("The message cursor must use UTC.");
        }
    }
}

public sealed record SendMessageRequest
{
    public Guid ClientMessageId { get; init; }

    [Required, StringLength(2000, MinimumLength = 1)]
    public required string Text { get; init; }
}

public sealed record ConversationDto(
    Guid Id,
    Guid? OriginatingReservationId,
    Guid CounterpartUserId,
    string CounterpartDisplayName,
    string CounterpartRole,
    Guid GymId,
    string GymName,
    string? LastMessageText,
    DateTime? LastMessageAtUtc,
    long UnreadCount,
    bool CanSend,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc);

public sealed record ChatMessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    Guid ClientMessageId,
    string Text,
    DateTime SentAtUtc);

public sealed record MessageHistoryDto(
    IReadOnlyList<ChatMessageDto> Items,
    bool HasMore,
    DateTime? NextBeforeSentAtUtc,
    Guid? NextBeforeId,
    bool CanSend);

public sealed record ConversationReadDto(
    long MarkedReadCount,
    DateTime ReadAtUtc);

public interface IChatService
{
    Task<ConversationDto> OpenAsync(
        OpenConversationRequest request,
        CancellationToken cancellationToken);

    Task<PagedResult<ConversationDto>> SearchMineAsync(
        ConversationSearchRequest request,
        CancellationToken cancellationToken);

    Task<MessageHistoryDto> GetMessagesAsync(
        Guid conversationId,
        MessageHistoryRequest request,
        CancellationToken cancellationToken);

    Task<ChatMessageDto> SendAsync(
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken);

    Task<ConversationReadDto> MarkReadAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

}

public interface IChatActorService
{
    Task EnsureParticipantAsync(
        Guid actorUserId,
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<ChatMessageDto> SendAsync(
        Guid actorUserId,
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken);

    Task<ConversationReadDto> MarkReadAsync(
        Guid actorUserId,
        Guid conversationId,
        CancellationToken cancellationToken);
}
