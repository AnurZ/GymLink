using System.ComponentModel.DataAnnotations;
using GymLink.Application.Common;

namespace GymLink.Application.Messaging;

public sealed record NotificationIntent(
    Guid RecipientUserId,
    Guid? TenantId,
    string Category,
    string Title,
    string Text,
    string? TargetType,
    Guid? TargetId,
    DateTime OccurredAtUtc,
    string CorrelationId);

public interface IOutboxWriter
{
    void AddNotification(NotificationIntent intent);

    void AddPasswordReset(
        Guid userId,
        Guid challengeId,
        DateTime occurredAtUtc,
        string correlationId);
}

public sealed record NotificationSearchRequest : PagedRequest
{
    public bool? IsRead { get; init; }

    [MaxLength(100)]
    public string? Category { get; init; }
}

public sealed record NotificationDto(
    Guid Id,
    string Category,
    string Title,
    string Text,
    DateTime CreatedAtUtc,
    bool IsRead,
    string? TargetType,
    Guid? TargetId,
    string ConcurrencyToken);

public sealed record UnreadNotificationCountDto(long Count);

public sealed record MarkNotificationReadRequest
{
    [Required]
    public required string ConcurrencyToken { get; init; }
}

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> SearchMineAsync(
        NotificationSearchRequest request,
        CancellationToken cancellationToken);

    Task<UnreadNotificationCountDto> GetUnreadCountAsync(CancellationToken cancellationToken);

    Task<NotificationDto> MarkReadAsync(
        Guid id,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken);

    Task MarkAllReadAsync(CancellationToken cancellationToken);
}
