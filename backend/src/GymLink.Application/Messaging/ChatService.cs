using System.ComponentModel.DataAnnotations;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Engagement;
using GymLink.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Messaging;

internal sealed class ChatService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    IOutboxWriter outbox,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider) : IChatService, IChatActorService
{
    public async Task<ConversationDto> OpenAsync(
        OpenConversationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ReservationId == Guid.Empty)
        {
            throw new ValidationException("Reservation ID is required.");
        }

        var userId = RequireUser();
        var context = await (
                from reservation in dbContext.AppointmentReservations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                join trainer in dbContext.TrainerProfiles
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    on new { reservation.TenantId, Id = reservation.TrainerProfileId }
                    equals new { trainer.TenantId, trainer.Id }
                where reservation.Id == request.ReservationId &&
                      (reservation.MemberUserId == userId || trainer.UserId == userId)
                select new ReservationChatContext(
                    reservation.TenantId,
                    reservation.Id,
                    reservation.MemberUserId,
                    trainer.UserId,
                    reservation.Status))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ConversationNotFound();

        var existingId = await dbContext.Conversations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.TenantId == context.TenantId &&
                x.MemberUserId == context.MemberUserId &&
                x.TrainerUserId == context.TrainerUserId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingId.HasValue)
        {
            return await LoadConversationAsync(existingId.Value, userId, cancellationToken);
        }

        if (context.Status is not ReservationStatus.Confirmed and
            not ReservationStatus.Completed)
        {
            throw new ConflictException(
                "conversation_relationship_required",
                "A confirmed or completed reservation is required to start a conversation.");
        }

        try
        {
            var conversationId = await transaction.ExecuteSerializableAsync(async ct =>
            {
                var duplicateId = await dbContext.Conversations
                    .IgnoreQueryFilters()
                    .Where(x =>
                        x.TenantId == context.TenantId &&
                        x.MemberUserId == context.MemberUserId &&
                        x.TrainerUserId == context.TrainerUserId)
                    .Select(x => (Guid?)x.Id)
                    .SingleOrDefaultAsync(ct);
                if (duplicateId.HasValue)
                {
                    return duplicateId.Value;
                }

                if (!await IsPairEligibleAsync(
                        context.TenantId,
                        context.MemberUserId,
                        context.TrainerUserId,
                        ct))
                {
                    throw new ConflictException(
                        "conversation_relationship_ineligible",
                        "Both participants must remain eligible to start a conversation.");
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var conversation = new Conversation(
                    context.TenantId,
                    context.ReservationId,
                    context.MemberUserId,
                    context.TrainerUserId,
                    now);
                var member = new ConversationParticipant(
                    context.TenantId,
                    conversation.Id,
                    context.MemberUserId,
                    now);
                var trainer = new ConversationParticipant(
                    context.TenantId,
                    conversation.Id,
                    context.TrainerUserId,
                    now);
                using (tenantMutationScope.Begin(context.TenantId))
                {
                    dbContext.Conversations.Add(conversation);
                    dbContext.ConversationParticipants.AddRange(member, trainer);
                    await dbContext.SaveChangesAsync(ct);
                }

                return conversation.Id;
            }, cancellationToken);
            return await LoadConversationAsync(conversationId, userId, cancellationToken);
        }
        catch (Exception exception) when (ContainsDuplicateWrite(exception))
        {
            dbContext.ClearTrackedChanges();
            var winnerId = await dbContext.Conversations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == context.TenantId &&
                    x.MemberUserId == context.MemberUserId &&
                    x.TrainerUserId == context.TrainerUserId)
                .Select(x => x.Id)
                .SingleAsync(cancellationToken);
            return await LoadConversationAsync(winnerId, userId, cancellationToken);
        }
    }

    public Task<PagedResult<ConversationDto>> SearchMineAsync(
        ConversationSearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var userId = RequireUser();
        var search = request.Search?.Trim();
        var query =
            from participant in dbContext.ConversationParticipants
                .IgnoreQueryFilters()
                .AsNoTracking()
            join conversation in dbContext.Conversations
                .IgnoreQueryFilters()
                .AsNoTracking()
                on new { participant.TenantId, Id = participant.ConversationId }
                equals new { conversation.TenantId, conversation.Id }
            join member in dbContext.UserProfiles.AsNoTracking()
                on conversation.MemberUserId equals member.Id
            join trainerUser in dbContext.UserProfiles.AsNoTracking()
                on conversation.TrainerUserId equals trainerUser.Id
            join gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                on conversation.TenantId equals gym.TenantId
            where participant.UserId == userId &&
                  (string.IsNullOrEmpty(search) ||
                   (userId == conversation.MemberUserId
                       ? trainerUser.DisplayName.Contains(search)
                       : member.DisplayName.Contains(search)) ||
                   gym.Name.Contains(search))
            let lastText = dbContext.Messages
                .IgnoreQueryFilters()
                .Where(x => x.ConversationId == conversation.Id)
                .OrderByDescending(x => x.SentAtUtc)
                .ThenByDescending(x => x.Id)
                .Select(x => x.Text)
                .FirstOrDefault()
            let unreadCount = dbContext.Messages
                .IgnoreQueryFilters()
                .LongCount(x =>
                    x.ConversationId == conversation.Id &&
                    x.SenderUserId != userId &&
                    (participant.LastReadAtUtc == null ||
                     x.SentAtUtc > participant.LastReadAtUtc))
            let canSend = participant.LeftAtUtc == null &&
                conversation.ClosedAtUtc == null &&
                member.IsActive &&
                trainerUser.IsActive &&
                dbContext.TrainerProfiles.IgnoreQueryFilters().Any(x =>
                    x.TenantId == conversation.TenantId &&
                    x.UserId == conversation.TrainerUserId &&
                    x.IsActive) &&
                dbContext.ConversationParticipants.IgnoreQueryFilters().Count(x =>
                    x.ConversationId == conversation.Id &&
                    x.LeftAtUtc == null) == 2 &&
                dbContext.UserGymAssignments.IgnoreQueryFilters().Any(x =>
                    x.TenantId == conversation.TenantId &&
                    x.UserId == conversation.MemberUserId &&
                    x.Role == RoleNames.Member &&
                    x.Status == AssignmentStatus.Active) &&
                dbContext.UserGymAssignments.IgnoreQueryFilters().Any(x =>
                    x.TenantId == conversation.TenantId &&
                    x.UserId == conversation.TrainerUserId &&
                    x.Role == RoleNames.Trainer &&
                    x.Status == AssignmentStatus.Active)
            orderby conversation.LastMessageAtUtc descending,
                conversation.CreatedAtUtc descending,
                conversation.Id descending
            select new ConversationDto(
                conversation.Id,
                conversation.ReservationId,
                userId == conversation.MemberUserId
                    ? conversation.TrainerUserId
                    : conversation.MemberUserId,
                userId == conversation.MemberUserId
                    ? trainerUser.DisplayName
                    : member.DisplayName,
                userId == conversation.MemberUserId
                    ? RoleNames.Trainer
                    : RoleNames.Member,
                gym.Id,
                gym.Name,
                lastText,
                conversation.LastMessageAtUtc,
                unreadCount,
                canSend,
                conversation.CreatedAtUtc,
                conversation.ClosedAtUtc);

        return query.ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<MessageHistoryDto> GetMessagesAsync(
        Guid conversationId,
        MessageHistoryRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var userId = RequireUser();
        var access = await LoadAccessAsync(conversationId, userId, cancellationToken);
        var query = dbContext.Messages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.TenantId == access.TenantId &&
                x.ConversationId == conversationId);
        if (request.BeforeSentAtUtc.HasValue)
        {
            var beforeUtc = request.BeforeSentAtUtc.Value;
            var beforeId = request.BeforeId!.Value;
            query = query.Where(x =>
                x.SentAtUtc < beforeUtc ||
                (x.SentAtUtc == beforeUtc && x.Id.CompareTo(beforeId) < 0));
        }

        var rows = await query
            .OrderByDescending(x => x.SentAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(request.Take + 1)
            .Select(x => new ChatMessageDto(
                x.Id,
                x.ConversationId,
                x.SenderUserId,
                x.ClientMessageId,
                x.Text,
                x.SentAtUtc))
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > request.Take;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var cursor = rows.LastOrDefault();
        rows.Reverse();
        return new(
            rows,
            hasMore,
            hasMore ? cursor?.SentAtUtc : null,
            hasMore ? cursor?.Id : null,
            await CanSendAsync(access, cancellationToken));
    }

    public async Task<ChatMessageDto> SendAsync(
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken) =>
        await SendAsync(RequireUser(), conversationId, request, cancellationToken);

    public async Task<ChatMessageDto> SendAsync(
        Guid actorUserId,
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty || request.ClientMessageId == Guid.Empty)
        {
            throw new ValidationException(
                "Conversation and client message IDs are required.");
        }

        var userId = RequireActor(actorUserId);
        var existing = await FindMessageAsync(
            conversationId,
            userId,
            request.ClientMessageId,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await transaction.ExecuteSerializableAsync(async ct =>
            {
                var duplicate = await FindMessageAsync(
                    conversationId,
                    userId,
                    request.ClientMessageId,
                    ct);
                if (duplicate is not null)
                {
                    return duplicate;
                }

                var access = await LoadAccessAsync(
                    conversationId,
                    userId,
                    ct,
                    tracked: true);
                if (!await CanSendAsync(access, ct))
                {
                    throw new ConflictException(
                        "conversation_read_only",
                        "The conversation is read-only because a participant is no longer eligible.");
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var message = new Message(
                    access.TenantId,
                    conversationId,
                    userId,
                    request.ClientMessageId,
                    request.Text,
                    now);
                access.Conversation.RecordMessage(now);
                using (tenantMutationScope.Begin(access.TenantId))
                {
                    dbContext.Messages.Add(message);
                    outbox.AddNotification(new(
                        access.CounterpartUserId,
                        access.TenantId,
                        "chat",
                        "Nova poruka",
                        $"Imate novu poruku od {access.CurrentDisplayName}.",
                        "conversation",
                        conversationId,
                        now,
                        requestMetadata.CorrelationId));
                    await dbContext.SaveChangesAsync(ct);
                }

                return Map(message);
            }, cancellationToken);
        }
        catch (Exception exception) when (ContainsDuplicateWrite(exception))
        {
            dbContext.ClearTrackedChanges();
            return await FindMessageAsync(
                    conversationId,
                    userId,
                    request.ClientMessageId,
                    cancellationToken)
                ?? throw new ConflictException(
                    "message_send_conflict",
                    "The message could not be reconciled after a concurrent send.");
        }
    }

    public async Task<ConversationReadDto> MarkReadAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        await MarkReadAsync(RequireUser(), conversationId, cancellationToken);

    public async Task<ConversationReadDto> MarkReadAsync(
        Guid actorUserId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var userId = RequireActor(actorUserId);
        var access = await LoadAccessAsync(
            conversationId,
            userId,
            cancellationToken,
            tracked: true);
        if (access.Participant.LeftAtUtc.HasValue)
        {
            throw new ConflictException(
                "conversation_read_only",
                "The conversation participation has ended.");
        }

        var previous = access.Participant.LastReadAtUtc;
        var unreadCount = await dbContext.Messages
            .IgnoreQueryFilters()
            .LongCountAsync(
                x => x.ConversationId == conversationId &&
                     x.SenderUserId != userId &&
                     (!previous.HasValue || x.SentAtUtc > previous.Value),
                cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        using (tenantMutationScope.Begin(access.TenantId))
        {
            access.Participant.MarkRead(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new(unreadCount, access.Participant.LastReadAtUtc!.Value);
    }

    public async Task EnsureParticipantAsync(
        Guid actorUserId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var access = await LoadAccessAsync(
            conversationId,
            RequireActor(actorUserId),
            cancellationToken);
        if (access.Participant.LeftAtUtc.HasValue)
        {
            throw new AuthorizationDeniedException(
                "conversation_participation_ended",
                "The conversation participation has ended.");
        }

        if (!await CanSendAsync(access, cancellationToken))
        {
            throw new AuthorizationDeniedException(
                "conversation_participation_revoked",
                "Realtime conversation participation is no longer allowed.");
        }
    }

    private async Task<ConversationDto> LoadConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await (
                from participant in dbContext.ConversationParticipants
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                join conversation in dbContext.Conversations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    on new { participant.TenantId, Id = participant.ConversationId }
                    equals new { conversation.TenantId, conversation.Id }
                join member in dbContext.UserProfiles.AsNoTracking()
                    on conversation.MemberUserId equals member.Id
                join trainerUser in dbContext.UserProfiles.AsNoTracking()
                    on conversation.TrainerUserId equals trainerUser.Id
                join gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                    on conversation.TenantId equals gym.TenantId
                where conversation.Id == conversationId &&
                      participant.UserId == userId
                let lastText = dbContext.Messages
                    .IgnoreQueryFilters()
                    .Where(x => x.ConversationId == conversation.Id)
                    .OrderByDescending(x => x.SentAtUtc)
                    .ThenByDescending(x => x.Id)
                    .Select(x => x.Text)
                    .FirstOrDefault()
                let unreadCount = dbContext.Messages
                    .IgnoreQueryFilters()
                    .LongCount(x =>
                        x.ConversationId == conversation.Id &&
                        x.SenderUserId != userId &&
                        (participant.LastReadAtUtc == null ||
                         x.SentAtUtc > participant.LastReadAtUtc))
                select new
                {
                    Conversation = conversation,
                    Participant = participant,
                    Member = member,
                    Trainer = trainerUser,
                    Gym = gym,
                    LastText = lastText,
                    UnreadCount = unreadCount,
                })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ConversationNotFound();
        var access = new ConversationAccess(
            result.Conversation.TenantId,
            result.Conversation,
            result.Participant,
            userId == result.Conversation.MemberUserId
                ? result.Conversation.TrainerUserId
                : result.Conversation.MemberUserId,
            userId == result.Conversation.MemberUserId
                ? result.Member.DisplayName
                : result.Trainer.DisplayName);
        return new(
            result.Conversation.Id,
            result.Conversation.ReservationId,
            access.CounterpartUserId,
            userId == result.Conversation.MemberUserId
                ? result.Trainer.DisplayName
                : result.Member.DisplayName,
            userId == result.Conversation.MemberUserId
                ? RoleNames.Trainer
                : RoleNames.Member,
            result.Gym.Id,
            result.Gym.Name,
            result.LastText,
            result.Conversation.LastMessageAtUtc,
            result.UnreadCount,
            await CanSendAsync(access, cancellationToken),
            result.Conversation.CreatedAtUtc,
            result.Conversation.ClosedAtUtc);
    }

    private async Task<ConversationAccess> LoadAccessAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken,
        bool tracked = false)
    {
        var conversations = dbContext.Conversations.IgnoreQueryFilters();
        var participants = dbContext.ConversationParticipants.IgnoreQueryFilters();
        var profiles = dbContext.UserProfiles.AsQueryable();
        if (!tracked)
        {
            conversations = conversations.AsNoTracking();
            participants = participants.AsNoTracking();
            profiles = profiles.AsNoTracking();
        }

        return await (
                from participant in participants
                join conversation in conversations
                    on new { participant.TenantId, Id = participant.ConversationId }
                    equals new { conversation.TenantId, conversation.Id }
                join currentProfile in profiles
                    on userId equals currentProfile.Id
                where conversation.Id == conversationId &&
                      participant.UserId == userId
                select new ConversationAccess(
                    conversation.TenantId,
                    conversation,
                    participant,
                    userId == conversation.MemberUserId
                        ? conversation.TrainerUserId
                        : conversation.MemberUserId,
                    currentProfile.DisplayName))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ConversationNotFound();
    }

    private async Task<bool> CanSendAsync(
        ConversationAccess access,
        CancellationToken cancellationToken)
    {
        if (access.Conversation.ClosedAtUtc.HasValue ||
            access.Participant.LeftAtUtc.HasValue)
        {
            return false;
        }

        if (!await IsPairEligibleAsync(
                access.TenantId,
                access.Conversation.MemberUserId,
                access.Conversation.TrainerUserId,
                cancellationToken))
        {
            return false;
        }

        var activeConversationParticipants = await dbContext.ConversationParticipants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                x => x.ConversationId == access.Conversation.Id &&
                     x.LeftAtUtc == null,
                cancellationToken);
        return activeConversationParticipants == 2;
    }

    private async Task<bool> IsPairEligibleAsync(
        Guid tenantId,
        Guid memberUserId,
        Guid trainerUserId,
        CancellationToken cancellationToken)
    {
        var participantsActive = await dbContext.UserProfiles
            .AsNoTracking()
            .CountAsync(
                x => (x.Id == memberUserId || x.Id == trainerUserId) &&
                     x.IsActive,
                cancellationToken) == 2;
        if (!participantsActive)
        {
            return false;
        }

        var trainerActive = await dbContext.TrainerProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                x => x.TenantId == tenantId &&
                     x.UserId == trainerUserId &&
                     x.IsActive,
                cancellationToken);
        var assignmentActive = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                x => x.TenantId == tenantId &&
                     x.UserId == trainerUserId &&
                     x.Role == RoleNames.Trainer &&
                     x.Status == AssignmentStatus.Active,
                cancellationToken);
        var memberAssignmentActive = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                x => x.TenantId == tenantId &&
                     x.UserId == memberUserId &&
                     x.Role == RoleNames.Member &&
                     x.Status == AssignmentStatus.Active,
                cancellationToken);
        return trainerActive && assignmentActive && memberAssignmentActive;
    }

    private Task<ChatMessageDto?> FindMessageAsync(
        Guid conversationId,
        Guid senderUserId,
        Guid clientMessageId,
        CancellationToken cancellationToken) =>
        dbContext.Messages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.ConversationId == conversationId &&
                x.SenderUserId == senderUserId &&
                x.ClientMessageId == clientMessageId)
            .Select(x => new ChatMessageDto(
                x.Id,
                x.ConversationId,
                x.SenderUserId,
                x.ClientMessageId,
                x.Text,
                x.SentAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    private Guid RequireUser() =>
        currentUser.IsAuthenticated && currentUser.UserId.HasValue
            ? currentUser.UserId.Value
            : throw new AuthenticationFailedException(
                "authentication_required",
                "Authentication is required.");

    private static Guid RequireActor(Guid actorUserId) =>
        actorUserId != Guid.Empty
            ? actorUserId
            : throw new AuthenticationFailedException(
                "authentication_required",
                "Authentication is required.");

    private static ChatMessageDto Map(Message message) =>
        new(
            message.Id,
            message.ConversationId,
            message.SenderUserId,
            message.ClientMessageId,
            message.Text,
            message.SentAtUtc);

    private static bool ContainsDuplicateWrite(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName == "Microsoft.Data.SqlClient.SqlException")
            {
                var number = current.GetType().GetProperty("Number")?.GetValue(current);
                if (number is 2601 or 2627)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static NotFoundException ConversationNotFound() =>
        new(
            "conversation_not_found",
            "The conversation was not found.");

    private sealed record ReservationChatContext(
        Guid TenantId,
        Guid ReservationId,
        Guid MemberUserId,
        Guid TrainerUserId,
        ReservationStatus Status);

    private sealed record ConversationAccess(
        Guid TenantId,
        Conversation Conversation,
        ConversationParticipant Participant,
        Guid CounterpartUserId,
        string CurrentDisplayName);
}
