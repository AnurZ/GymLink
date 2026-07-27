using GymLink.Application.Memberships;
using Microsoft.Extensions.Logging;

namespace GymLink.Infrastructure.Memberships;

internal sealed class LoggingMembershipWorkflowEventRecorder(
    ILogger<LoggingMembershipWorkflowEventRecorder> logger)
    : IMembershipWorkflowEventRecorder
{
    private static readonly Action<ILogger, string, Guid, Guid, Guid, DateTime, Exception?>
        LogIntent = LoggerMessage.Define<string, Guid, Guid, Guid, DateTime>(
            LogLevel.Information,
            new EventId(400, "MembershipWorkflowEventIntent"),
            "Membership workflow intent {EventName}: tenant {TenantId}, member {MemberUserId}, aggregate {AggregateId}, occurred {OccurredAtUtc}.");

    public void Record(MembershipWorkflowEventIntent intent) =>
        LogIntent(
            logger,
            intent.Name,
            intent.TenantId,
            intent.MemberUserId,
            intent.AggregateId,
            intent.OccurredAtUtc,
            null);
}
