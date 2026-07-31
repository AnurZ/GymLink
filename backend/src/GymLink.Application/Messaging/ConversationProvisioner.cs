using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Domain.Engagement;
using GymLink.Domain.Enums;
using GymLink.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Messaging;

internal sealed class ConversationProvisioner(
    IApplicationDbContext dbContext,
    IConversationPairLock pairLock,
    TimeProvider timeProvider) : IConversationProvisioner
{
    public async Task<ConversationProvisioningResult> EnsureForConfirmedReservationAsync(
        AppointmentReservation reservation,
        CancellationToken cancellationToken)
    {
        if (reservation.Status is not ReservationStatus.Confirmed and
            not ReservationStatus.Completed)
        {
            throw new ConflictException(
                "conversation_relationship_required",
                "A confirmed or completed reservation is required for a conversation.");
        }

        var trainerUserId = await dbContext.TrainerProfiles
            .IgnoreQueryFilters()
            .Where(x =>
                x.Id == reservation.TrainerProfileId &&
                x.TenantId == reservation.TenantId)
            .Select(x => (Guid?)x.UserId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(
                "trainer_not_found",
                "The reservation trainer was not found.");
        await pairLock.AcquireAsync(
            reservation.TenantId,
            reservation.MemberUserId,
            trainerUserId,
            cancellationToken);
        var existingId = await dbContext.Conversations
            .IgnoreQueryFilters()
            .Where(x =>
                x.TenantId == reservation.TenantId &&
                x.MemberUserId == reservation.MemberUserId &&
                x.TrainerUserId == trainerUserId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingId.HasValue)
        {
            return new(
                existingId.Value,
                reservation.MemberUserId,
                trainerUserId,
                false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var conversation = new Conversation(
            reservation.TenantId,
            reservation.Id,
            reservation.MemberUserId,
            trainerUserId,
            now);
        dbContext.Conversations.Add(conversation);
        dbContext.ConversationParticipants.AddRange(
            new ConversationParticipant(
                reservation.TenantId,
                conversation.Id,
                reservation.MemberUserId,
                now),
            new ConversationParticipant(
                reservation.TenantId,
                conversation.Id,
                trainerUserId,
                now));
        return new(
            conversation.Id,
            reservation.MemberUserId,
            trainerUserId,
            true);
    }
}

internal sealed class NullConversationRealtimeNotifier :
    IConversationRealtimeNotifier
{
    public Task ConversationAvailableAsync(
        ConversationProvisioningResult conversation,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
