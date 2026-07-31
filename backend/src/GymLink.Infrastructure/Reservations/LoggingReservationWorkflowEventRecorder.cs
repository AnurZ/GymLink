using GymLink.Application.Reservations;
using GymLink.Application.Messaging;
using GymLink.Application.Abstractions;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Infrastructure.Reservations;

internal sealed class LoggingReservationWorkflowEventRecorder(
    GymLinkDbContext dbContext,
    IOutboxWriter outbox,
    IRequestMetadata requestMetadata)
    : IReservationWorkflowEventRecorder
{
    public async Task RecordAsync(
        ReservationWorkflowEventIntent intent,
        CancellationToken cancellationToken)
    {
        var recipients = new HashSet<Guid>();
        var reservation = dbContext.AppointmentReservations.Local
            .FirstOrDefault(x => x.Id == intent.TargetId);
        reservation ??= await dbContext.AppointmentReservations
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == intent.TargetId, cancellationToken);
        if (reservation is not null)
        {
            recipients.Add(reservation.MemberUserId);
            var trainerUserId = await dbContext.TrainerProfiles
                .IgnoreQueryFilters()
                .Where(x => x.Id == reservation.TrainerProfileId)
                .Select(x => (Guid?)x.UserId)
                .SingleOrDefaultAsync(cancellationToken);
            if (trainerUserId.HasValue)
            {
                recipients.Add(trainerUserId.Value);
            }
        }

        if (intent.Name == "review.trainer_created")
        {
            var trainerProfileId = dbContext.Reviews.Local
                .FirstOrDefault(x => x.Id == intent.TargetId)?.TrainerProfileId;
            if (trainerProfileId.HasValue)
            {
                var trainerUserId = await dbContext.TrainerProfiles
                    .IgnoreQueryFilters()
                    .Where(x => x.Id == trainerProfileId.Value)
                    .Select(x => (Guid?)x.UserId)
                    .SingleOrDefaultAsync(cancellationToken);
                if (trainerUserId.HasValue)
                {
                    recipients.Add(trainerUserId.Value);
                }
            }
        }

        var administrators = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .Where(x =>
                x.TenantId == intent.TenantId &&
                x.Role == RoleNames.GymAdmin &&
                x.Status == AssignmentStatus.Active)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);
        recipients.UnionWith(administrators);
        if (recipients.Count == 0)
        {
            recipients.Add(intent.ActorUserId);
        }

        foreach (var recipient in recipients)
        {
            outbox.AddNotification(new(
                recipient,
                intent.TenantId,
                intent.Name,
                Title(intent.Name),
                Text(intent.Name),
                intent.Name.StartsWith("availability", StringComparison.Ordinal)
                    ? "availability"
                    : intent.Name.StartsWith("review", StringComparison.Ordinal)
                        ? "review"
                        : "reservation",
                intent.TargetId,
                intent.OccurredAtUtc,
                requestMetadata.CorrelationId));
        }
    }

    private static string Title(string name) =>
        name.StartsWith("availability", StringComparison.Ordinal)
            ? "Dostupnost trenera"
            : name.StartsWith("review", StringComparison.Ordinal)
                ? "Nova recenzija"
                : name == "reservation.confirmed_pay_in_person"
                    ? "Termin potvrđen"
                    : "Rezervacija";

    private static string Text(string name) =>
        name switch
        {
            "reservation.created" => "Kreirana je nova rezervacija termina.",
            "reservation.confirmed_pay_in_person" =>
                "Termin je potvrđen. Plaćanje se vrši uživo na treningu.",
            "reservation.status_changed" => "Status rezervacije je promijenjen.",
            "review.trainer_created" => "Objavljena je nova recenzija trenera.",
            "review.gym_created" => "Objavljena je nova recenzija teretane.",
            _ => "Dostupnost trenera je ažurirana.",
        };
}
