using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Messaging;

internal sealed class NotificationService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : INotificationService
{
    public Task<PagedResult<NotificationDto>> SearchMineAsync(
        NotificationSearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var userId = RequireUser();
        var query = dbContext.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == userId);
        if (request.IsRead.HasValue)
        {
            query = request.IsRead.Value
                ? query.Where(x => x.ReadAtUtc != null)
                : query.Where(x => x.ReadAtUtc == null);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = request.Category.Trim();
            query = query.Where(x => x.Type == category);
        }

        return query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new NotificationDto(
                x.Id,
                x.Type,
                x.Title,
                x.Text,
                x.CreatedAtUtc,
                x.ReadAtUtc != null,
                x.TargetType,
                x.TargetId,
                Convert.ToBase64String(x.RowVersion)))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<UnreadNotificationCountDto> GetUnreadCountAsync(
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        return new(await dbContext.Notifications.LongCountAsync(
            x => x.RecipientUserId == userId && x.ReadAtUtc == null,
            cancellationToken));
    }

    public async Task<NotificationDto> MarkReadAsync(
        Guid id,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var notification = await dbContext.Notifications.SingleOrDefaultAsync(
            x => x.Id == id && x.RecipientUserId == userId,
            cancellationToken)
            ?? throw new NotFoundException(
                "notification_not_found",
                "The notification was not found.");
        EnsureConcurrency(notification.RowVersion, request.ConcurrencyToken);
        notification.MarkRead(timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(
            notification.Id,
            notification.Type,
            notification.Title,
            notification.Text,
            notification.CreatedAtUtc,
            true,
            notification.TargetType,
            notification.TargetId,
            Convert.ToBase64String(notification.RowVersion));
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.Notifications
            .Where(x => x.RecipientUserId == userId && x.ReadAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.ReadAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now)
                    .SetProperty(x => x.UpdatedByUserId, userId),
                cancellationToken);
    }

    private Guid RequireUser() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException(
            "authentication_required",
            "Authentication is required.");

    private static void EnsureConcurrency(byte[] current, string supplied)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(supplied);
        }
        catch (FormatException)
        {
            throw Conflict();
        }

        if (!current.SequenceEqual(decoded))
        {
            throw Conflict();
        }
    }

    private static ConflictException Conflict() =>
        new(
            "concurrency_conflict",
            "The record was changed by another request. Reload it and try again.");
}
