using GymLink.Application.Abstractions;
using GymLink.Application.Memberships;
using GymLink.Application.Messaging;
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
        var details = await LoadDetailsAsync(intent, cancellationToken);
        var administrators = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == intent.TenantId &&
                        x.Role == RoleNames.GymAdmin &&
                        x.Status == AssignmentStatus.Active)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);
        var recipients = administrators.Append(intent.MemberUserId).Distinct();

        foreach (var recipient in recipients)
        {
            var isMember = recipient == intent.MemberUserId;
            outbox.AddNotification(new(
                recipient,
                intent.TenantId,
                intent.Name,
                intent.Name.Contains("request", StringComparison.Ordinal)
                    ? "Zahtjev za članstvo"
                    : "Članstvo",
                Format(intent.Name, details, isMember),
                "membership",
                intent.AggregateId,
                intent.OccurredAtUtc,
                requestMetadata.CorrelationId));
        }
    }

    public async Task RecordManyAsync(
        IReadOnlyCollection<MembershipWorkflowEventIntent> intents,
        CancellationToken cancellationToken)
    {
        foreach (var intent in intents)
        {
            await RecordAsync(intent, cancellationToken);
        }
    }

    private async Task<MembershipNotificationDetails> LoadDetailsAsync(
        MembershipWorkflowEventIntent intent,
        CancellationToken cancellationToken)
    {
        var membership = dbContext.Memberships.Local
            .FirstOrDefault(x => x.Id == intent.AggregateId);
        membership ??= await dbContext.Memberships.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == intent.AggregateId, cancellationToken);

        var requestId = membership?.MembershipRequestId ?? intent.AggregateId;
        var request = dbContext.MembershipRequests.Local.FirstOrDefault(x => x.Id == requestId);
        request ??= await dbContext.MembershipRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        var gymId = membership?.GymId ?? request?.GymId;
        var planId = membership?.MembershipPlanId ?? request?.MembershipPlanId;

        var gymName = gymId.HasValue
            ? await dbContext.Gyms.IgnoreQueryFilters().Where(x => x.Id == gymId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        var planName = membership?.PlanName;
        planName ??= planId.HasValue
            ? await dbContext.MembershipPlans.IgnoreQueryFilters().Where(x => x.Id == planId)
                .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        var memberName = await dbContext.UserProfiles.IgnoreQueryFilters()
            .Where(x => x.Id == intent.MemberUserId)
            .Select(x => x.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
        return new(
            memberName ?? "Korisnik",
            gymName ?? "Teretana",
            planName ?? "članstva",
            membership?.StatusReason ?? request?.DecisionReason,
            request?.PaymentMethod,
            (membership?.StatusChangedByUserId ?? request?.DecidedByUserId) ==
                intent.MemberUserId);
    }

    internal static string Format(
        string name,
        MembershipNotificationDetails details,
        bool isMember)
    {
        var reason = string.IsNullOrWhiteSpace(details.Reason)
            ? string.Empty
            : $" Razlog: {details.Reason}";
        if (isMember)
        {
            return name switch
            {
                "membership.requested" => $"{details.GymName}: Zahtjev za članstvo {details.PlanName} je poslan.",
                "membership.request.cancelled" => $"{details.GymName}: Zahtjev za članstvo {details.PlanName} je otkazan.",
                "membership.approved" => $"{details.GymName}: Vaše članstvo {details.PlanName} je odobreno.",
                "membership.rejected" => $"{details.GymName}: Zahtjev za članstvo {details.PlanName} je odbijen.{reason}",
                "membership.cancelled" => $"{details.GymName}: Vaše članstvo {details.PlanName} je otkazano.{reason}",
                "membership.suspended" => $"{details.GymName}: Vaše članstvo {details.PlanName} je suspendovano.{reason}",
                "membership.reactivated" => $"{details.GymName}: Vaše članstvo {details.PlanName} je ponovo aktivno.",
                "membership.expired" => $"{details.GymName}: Vaše članstvo {details.PlanName} je isteklo.",
                _ => $"{details.GymName}: Status članstva {details.PlanName} je ažuriran.",
            };
        }

        var payment = details.PaymentMethod.HasValue
            ? $" Način plaćanja: {PaymentMethod(details.PaymentMethod.Value)}."
            : string.Empty;
        return name switch
        {
            "membership.requested" => $"{details.MemberName} je poslao zahtjev za članstvo {details.PlanName} u teretani {details.GymName}.{payment}",
            "membership.request.cancelled" => $"{details.MemberName} je otkazao zahtjev za članstvo {details.PlanName} u teretani {details.GymName}.",
            "membership.approved" => $"Članstvo {details.PlanName} za korisnika {details.MemberName} u teretani {details.GymName} je odobreno.",
            "membership.rejected" => $"Zahtjev korisnika {details.MemberName} za članstvo {details.PlanName} u teretani {details.GymName} je odbijen.{reason}",
            "membership.cancelled" => details.ChangedByMember
                ? $"{details.MemberName} je otkazao članstvo {details.PlanName} u teretani {details.GymName}.{reason}"
                : $"Članstvo {details.PlanName} korisnika {details.MemberName} u teretani {details.GymName} je otkazano.{reason}",
            "membership.suspended" => $"Članstvo {details.PlanName} korisnika {details.MemberName} u teretani {details.GymName} je suspendovano.{reason}",
            "membership.reactivated" => $"Članstvo {details.PlanName} korisnika {details.MemberName} u teretani {details.GymName} je ponovo aktivno.",
            "membership.expired" => $"Članstvo {details.PlanName} korisnika {details.MemberName} u teretani {details.GymName} je isteklo.",
            _ => $"Status članstva {details.PlanName} korisnika {details.MemberName} u teretani {details.GymName} je ažuriran.",
        };
    }

    private static string PaymentMethod(MembershipPaymentMethod method) => method switch
    {
        MembershipPaymentMethod.PayInPerson => "plaćanje uživo",
        MembershipPaymentMethod.StripeFallback => "kartično plaćanje (test)",
        _ => "kartično plaćanje",
    };

    internal sealed record MembershipNotificationDetails(
        string MemberName,
        string GymName,
        string PlanName,
        string? Reason,
        MembershipPaymentMethod? PaymentMethod,
        bool ChangedByMember);
}
