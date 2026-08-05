using GymLink.Application.Abstractions;
using GymLink.Domain.Enums;
using GymLink.Domain.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Recommendations;

internal sealed class PaymentWorkerRecommendationActivityRecorder(
    IApplicationDbContext dbContext) : IRecommendationActivityRecorder
{
    public Task RecordReadAsync(
        ActivityEventType eventType,
        Guid? targetTenantId,
        RecommendationTargetType? targetType,
        Guid? targetId,
        CancellationToken cancellationToken) => Task.CompletedTask;

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
