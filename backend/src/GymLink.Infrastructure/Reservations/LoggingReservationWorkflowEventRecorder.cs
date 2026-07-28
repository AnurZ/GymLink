using GymLink.Application.Reservations;
using Microsoft.Extensions.Logging;

namespace GymLink.Infrastructure.Reservations;

internal sealed class LoggingReservationWorkflowEventRecorder(
    ILogger<LoggingReservationWorkflowEventRecorder> logger)
    : IReservationWorkflowEventRecorder
{
    private static readonly Action<ILogger, string, Guid, Guid, Guid, DateTime, Exception?>
        LogIntent = LoggerMessage.Define<string, Guid, Guid, Guid, DateTime>(
            LogLevel.Information,
            new EventId(500, "ReservationWorkflowEventIntent"),
            "Reservation workflow intent {EventName}: tenant {TenantId}, actor {ActorUserId}, target {TargetId}, occurred {OccurredAtUtc}.");

    public void Record(ReservationWorkflowEventIntent intent) =>
        LogIntent(
            logger,
            intent.Name,
            intent.TenantId,
            intent.ActorUserId,
            intent.TargetId,
            intent.OccurredAtUtc,
            null);
}
