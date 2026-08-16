using System.Globalization;
using GymLink.Application.Abstractions;
using GymLink.Application.Messaging;
using GymLink.Application.Reservations;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Reservations;
using GymLink.Domain.Trainers;
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
        var reservation = dbContext.AppointmentReservations.Local
            .FirstOrDefault(x => x.Id == intent.TargetId);
        reservation ??= await dbContext.AppointmentReservations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == intent.TargetId, cancellationToken);
        var details = reservation is null
            ? null
            : await LoadReservationDetailsAsync(reservation, cancellationToken);

        var administrators = await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .Where(x => x.TenantId == intent.TenantId &&
                        x.Role == RoleNames.GymAdmin &&
                        x.Status == AssignmentStatus.Active)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);
        var recipients = new HashSet<Guid>(administrators);
        if (details is not null)
        {
            recipients.Add(reservation!.MemberUserId);
            recipients.Add(details.TrainerUserId);
        }
        else if (intent.Name == "review.trainer_created")
        {
            var review = dbContext.Reviews.Local.FirstOrDefault(x => x.Id == intent.TargetId);
            if (review is not null)
            {
                var trainerUserId = await dbContext.TrainerProfiles.IgnoreQueryFilters()
                    .Where(x => x.Id == review.TrainerProfileId)
                    .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(cancellationToken);
                if (trainerUserId.HasValue) recipients.Add(trainerUserId.Value);
            }
        }
        if (recipients.Count == 0) recipients.Add(intent.ActorUserId);

        foreach (var recipient in recipients)
        {
            var role = details is null || administrators.Contains(recipient)
                ? NotificationRole.GymAdmin
                : recipient == reservation!.MemberUserId
                    ? NotificationRole.Member
                    : NotificationRole.Trainer;
            var text = details is not null
                ? FormatReservation(intent.Name, details, role)
                : await FormatNonReservationAsync(intent, cancellationToken);
            outbox.AddNotification(new(
                recipient,
                intent.TenantId,
                Category(intent.Name, reservation?.Status),
                Title(intent.Name),
                text,
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

    private async Task<ReservationNotificationDetails> LoadReservationDetailsAsync(
        AppointmentReservation reservation,
        CancellationToken cancellationToken) =>
        await (
            from trainer in dbContext.TrainerProfiles.IgnoreQueryFilters().AsNoTracking()
            join trainerUser in dbContext.UserProfiles.AsNoTracking() on trainer.UserId equals trainerUser.Id
            join offering in dbContext.TrainerServiceOfferings.IgnoreQueryFilters().AsNoTracking()
                on reservation.TrainerServiceOfferingId equals offering.Id
            from member in dbContext.UserProfiles.AsNoTracking()
                .Where(x => x.Id == reservation.MemberUserId)
            from gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == reservation.TenantId)
            where trainer.Id == reservation.TrainerProfileId
            select new ReservationNotificationDetails(
                member.DisplayName,
                trainerUser.DisplayName,
                trainer.UserId,
                gym.Name,
                offering.Name,
                reservation.StartsAtUtc,
                reservation.Status,
                reservation.CancellationReason))
        .SingleAsync(cancellationToken);

    internal static string FormatReservation(
        string eventName,
        ReservationNotificationDetails details,
        NotificationRole role)
    {
        var local = Sarajevo(details.StartsAtUtc);
        var date = local.ToString("dd.MM.yyyy.", CultureInfo.GetCultureInfo("bs-BA"));
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var status = eventName switch
        {
            "reservation.confirmed_stripe" => "uspješno plaćen i potvrđen",
            "reservation.confirmed_pay_in_person" => "potvrđen",
            _ => details.Status switch
            {
                ReservationStatus.Confirmed => "potvrđen",
                ReservationStatus.Completed => "završen",
                ReservationStatus.Cancelled => "otkazan",
                _ => "kreiran i čeka plaćanje",
            },
        };
        var reason = string.IsNullOrWhiteSpace(details.Reason)
            ? string.Empty
            : $" Razlog: {details.Reason}";
        return role switch
        {
            NotificationRole.Member =>
                $"{details.GymName}: Termin kod trenera {details.TrainerName}: {details.OfferingName}, {date} u {time}, je {status}.{reason}",
            NotificationRole.Trainer =>
                $"Termin sa korisnikom {details.MemberName}: {details.OfferingName}, {date} u {time}, je {status}.{reason}",
            _ =>
                $"Termin korisnika {details.MemberName} kod trenera {details.TrainerName}: {details.OfferingName}, {date} u {time}, je {status}.{reason}",
        };
    }

    private async Task<string> FormatNonReservationAsync(
        ReservationWorkflowEventIntent intent,
        CancellationToken cancellationToken)
    {
        var gymName = await dbContext.Gyms.IgnoreQueryFilters()
            .Where(x => x.TenantId == intent.TenantId)
            .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken) ?? "teretani";
        if (intent.Name == "review.trainer_created")
        {
            var review = dbContext.Reviews.Local.FirstOrDefault(x => x.Id == intent.TargetId)
                ?? await dbContext.Reviews.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.Id == intent.TargetId, cancellationToken);
            return FormatTrainerReview(review.Rating, gymName);
        }
        if (intent.Name == "review.gym_created")
        {
            var review = dbContext.GymReviews.Local.FirstOrDefault(x => x.Id == intent.TargetId)
                ?? await dbContext.GymReviews.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.Id == intent.TargetId, cancellationToken);
            var reviewer = await dbContext.UserProfiles.Where(x => x.Id == review.ReviewerUserId)
                .Select(x => x.DisplayName).SingleAsync(cancellationToken);
            return $"{reviewer} je ocijenio teretanu {gymName} sa {review.Rating}/5.";
        }
        var trainerId = dbContext.TrainerAvailabilitySlots.Local
            .FirstOrDefault(x => x.Id == intent.TargetId)?.TrainerProfileId
            ?? dbContext.TrainerAvailabilitySchedules.Local
                .FirstOrDefault(x => x.Id == intent.TargetId)?.TrainerProfileId;
        var trainerName = trainerId.HasValue
            ? await (from trainer in dbContext.TrainerProfiles.IgnoreQueryFilters()
                     join user in dbContext.UserProfiles on trainer.UserId equals user.Id
                     where trainer.Id == trainerId
                     select user.DisplayName).SingleOrDefaultAsync(cancellationToken)
            : null;
        return $"Dostupnost trenera {trainerName ?? "trenera"} u teretani {gymName} je ažurirana.";
    }

    internal static string FormatTrainerReview(int rating, string gymName) =>
        $"Jedna od trenerskih sesija u teretani {gymName} ocijenjena je ocjenom {rating}/5.";

    private static DateTime Sarajevo(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(
        utc,
        TimeZoneInfo.FindSystemTimeZoneById(TrainerAvailabilitySchedule.SarajevoTimeZoneId));

    private static string Title(string name) =>
        name.StartsWith("availability", StringComparison.Ordinal) ? "Dostupnost trenera" :
        name.StartsWith("review", StringComparison.Ordinal) ? "Nova recenzija" : "Rezervacija";

    private static string Category(string eventName, ReservationStatus? status) =>
        eventName switch
        {
            "reservation.confirmed_stripe" or "reservation.confirmed_pay_in_person" =>
                "reservation.confirmed",
            "reservation.status_changed" when status == ReservationStatus.Completed =>
                "reservation.completed",
            "reservation.status_changed" when status == ReservationStatus.Cancelled =>
                "reservation.cancelled",
            _ => eventName,
        };

    internal enum NotificationRole { Member, Trainer, GymAdmin }

    internal sealed record ReservationNotificationDetails(
        string MemberName,
        string TrainerName,
        Guid TrainerUserId,
        string GymName,
        string OfferingName,
        DateTime StartsAtUtc,
        ReservationStatus Status,
        string? Reason);
}
