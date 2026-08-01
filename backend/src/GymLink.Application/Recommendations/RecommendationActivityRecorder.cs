using GymLink.Application.Abstractions;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymLink.Application.Recommendations;

internal sealed class RecommendationActivityRecorder(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    IIdentityAccountManager accounts,
    TimeProvider timeProvider,
    ILogger<RecommendationActivityRecorder> logger) : IRecommendationActivityRecorder
{
    private static readonly TimeSpan ReadDeduplicationWindow = TimeSpan.FromMinutes(15);
    private static readonly Action<ILogger, Guid, Exception?> LogReadSignalFailure =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1001, "RecommendationReadSignalFailure"),
            "Recommendation read signal could not be persisted for user {UserId}.");

    public async Task RecordReadAsync(
        ActivityEventType eventType,
        Guid? targetTenantId,
        RecommendationTargetType? targetType,
        Guid? targetId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
        {
            return;
        }

        var userId = currentUser.UserId.Value;
        try
        {
            if (!await accounts.IsInRoleAsync(userId, RoleNames.Member))
            {
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var cutoff = now - ReadDeduplicationWindow;
            if (await dbContext.ActivityHistory.AsNoTracking().AnyAsync(
                    x => x.UserId == userId &&
                         x.EventType == eventType &&
                         x.TargetType == targetType &&
                         x.TargetId == targetId &&
                         x.OccurredAtUtc >= cutoff,
                    cancellationToken))
            {
                return;
            }

            dbContext.ActivityHistory.Add(new ActivityHistory
            {
                UserId = userId,
                TargetTenantId = targetTenantId,
                TargetType = targetType,
                TargetId = targetId,
                EventType = eventType,
                OccurredAtUtc = now,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dbContext.ClearTrackedChanges();
            LogReadSignalFailure(logger, userId, exception);
        }
    }

    public async Task RecordWorkflowAsync(
        Guid userId,
        Guid? targetTenantId,
        RecommendationTargetType? targetType,
        Guid? targetId,
        ActivityEventType eventType,
        Guid sourceId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        if (sourceId == Guid.Empty || await dbContext.ActivityHistory.AnyAsync(
                x => x.UserId == userId &&
                     x.EventType == eventType &&
                     x.SourceId == sourceId,
                cancellationToken))
        {
            return;
        }

        dbContext.ActivityHistory.Add(new ActivityHistory
        {
            UserId = userId,
            TargetTenantId = targetTenantId,
            TargetType = targetType,
            TargetId = targetId,
            SourceId = sourceId,
            EventType = eventType,
            OccurredAtUtc = occurredAtUtc,
        });
    }
}
