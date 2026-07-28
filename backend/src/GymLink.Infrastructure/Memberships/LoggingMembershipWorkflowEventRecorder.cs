using GymLink.Application.Memberships;
using GymLink.Application.Messaging;
using GymLink.Application.Abstractions;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Infrastructure.Memberships;

internal sealed class LoggingMembershipWorkflowEventRecorder(
    GymLinkDbContext dbContext,
    IOutboxWriter outbox,
    IRequestMetadata requestMetadata)
    : IMembershipWorkflowEventRecorder
{
    public async Task RecordAsync(
        MembershipWorkflowEventIntent intent,
        CancellationToken cancellationToken)
    {
        var recipients = new HashSet<Guid> { intent.MemberUserId };
        if (intent.Name is "membership.requested" or
            "membership.request.cancelled" or
            "membership.cancelled")
        {
            var administrators = await dbContext.UserGymAssignments
                .IgnoreQueryFilters()
                .Where(x =>
                    x.TenantId == intent.TenantId &&
                    x.Role == RoleNames.GymAdmin &&
                    x.Status == AssignmentStatus.Active)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);
            recipients.UnionWith(administrators);
        }

        foreach (var recipient in recipients)
        {
            outbox.AddNotification(new(
                recipient,
                intent.TenantId,
                intent.Name,
                Title(intent.Name),
                Text(intent.Name),
                "membership",
                intent.AggregateId,
                intent.OccurredAtUtc,
                requestMetadata.CorrelationId));
        }
    }

    private static string Title(string name) =>
        name.Contains("request", StringComparison.Ordinal)
            ? "Zahtjev za članstvo"
            : "Članstvo";

    private static string Text(string name) =>
        name switch
        {
            "membership.approved" => "Vaš zahtjev za članstvo je odobren.",
            "membership.rejected" => "Vaš zahtjev za članstvo je odbijen.",
            "membership.suspended" => "Vaše članstvo je suspendovano.",
            "membership.reactivated" => "Vaše članstvo je ponovo aktivno.",
            "membership.expired" => "Vaše članstvo je isteklo.",
            "membership.cancelled" => "Članstvo je otkazano.",
            _ => "Status članstva je ažuriran.",
        };
}
