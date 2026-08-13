using System.ComponentModel.DataAnnotations;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Images;
using GymLink.Domain.Common;
using GymLink.Domain.Engagement;
using GymLink.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymLink.Application.Messaging;

internal sealed class ChatService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    IOutboxWriter outbox,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider,
    IFileStorage fileStorage,
    ILogger<ChatService> logger) : IChatService, IChatActorService
{
    private static readonly Action<ILogger, string, Exception?> LogOrphanDeleteFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, "ChatImageOrphanDeleteFailed"),
            "Failed to delete orphaned chat image {StorageKey}.");

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
            let trainerImageUrl = dbContext.TrainerProfiles
                .IgnoreQueryFilters()
                .Where(x =>
                    x.TenantId == conversation.TenantId &&
                    x.UserId == conversation.TrainerUserId)
                .Select(x => x.ImageUrl)
                .FirstOrDefault()
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
                userId == conversation.MemberUserId ? trainerImageUrl : null,
                gym.Id,
                gym.Name,
                lastText,
                conversation.LastMessageAtUtc,
                unreadCount,
                true,
                conversation.CreatedAtUtc,
                conversation.ClosedAtUtc);

        return query.ToPagedResultAsync(request, cancellationToken);
    }

    public Task<ConversationDto> GetMineAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        LoadConversationAsync(conversationId, RequireUser(), cancellationToken);

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

        var messages = await query
            .OrderByDescending(x => x.SentAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(request.Take + 1)
            .ToListAsync(cancellationToken);
        var hasMore = messages.Count > request.Take;
        if (hasMore)
        {
            messages.RemoveAt(messages.Count - 1);
        }

        var cursor = messages.LastOrDefault();
        var rows = messages.Select(Map).ToList();
        rows.Reverse();
        return new(
            rows,
            hasMore,
            hasMore ? cursor?.SentAtUtc : null,
            hasMore ? cursor?.Id : null,
            await CanSendAsync(access, cancellationToken));
    }

    public async Task<ChatMessageDto> SendImageAsync(
        Guid conversationId,
        ChatImageUpload upload,
        CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty || upload.ClientMessageId == Guid.Empty)
        {
            throw new ValidationException(
                "Conversation and client message IDs are required.");
        }

        var userId = RequireUser();
        var existing = await FindMessageAsync(
            conversationId,
            userId,
            upload.ClientMessageId,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contentType = ImageUploadValidator.Validate(
            upload.Content,
            upload.ContentType,
            upload.FileName,
            Message.MaximumImageFileSizeBytes,
            "invalid_chat_image");
        await using var content = new MemoryStream(upload.Content, writable: false);
        var stored = await fileStorage.SaveAsync(
            FileStorageArea.ChatImages,
            content,
            contentType,
            upload.FileName,
            cancellationToken);

        try
        {
            var result = await transaction.ExecuteSerializableAsync(async ct =>
            {
                var duplicate = await FindMessageAsync(
                    conversationId,
                    userId,
                    upload.ClientMessageId,
                    ct);
                if (duplicate is not null)
                {
                    return (Message: duplicate, Created: false);
                }

                var access = await LoadAccessAsync(
                    conversationId,
                    userId,
                    ct,
                    tracked: true);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var message = Message.CreateImage(
                    access.TenantId,
                    conversationId,
                    userId,
                    upload.ClientMessageId,
                    stored.StorageKey,
                    contentType,
                    upload.Content.LongLength,
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

                return (Message: Map(message), Created: true);
            }, cancellationToken);
            if (!result.Created)
            {
                await DeleteImageQuietlyAsync(stored.StorageKey);
            }

            return result.Message;
        }
        catch (Exception exception) when (ContainsDuplicateWrite(exception))
        {
            dbContext.ClearTrackedChanges();
            await DeleteImageQuietlyAsync(stored.StorageKey);
            return await FindMessageAsync(
                    conversationId,
                    userId,
                    upload.ClientMessageId,
                    cancellationToken)
                ?? throw new ConflictException(
                    "message_send_conflict",
                    "The message could not be reconciled after a concurrent send.");
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            await DeleteImageQuietlyAsync(stored.StorageKey);
            throw;
        }
    }

    public async Task<ChatImageContent> GetImageAsync(
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var access = await LoadAccessAsync(conversationId, userId, cancellationToken);
        var image = await dbContext.Messages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.TenantId == access.TenantId &&
                x.ConversationId == conversationId &&
                x.Id == messageId &&
                x.ImageStorageKey != null)
            .Select(x => new
            {
                StorageKey = x.ImageStorageKey!,
                ContentType = x.ImageContentType!,
                FileSize = x.ImageFileSizeBytes!.Value,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw MessageImageNotFound();
        var stream = await fileStorage.OpenReadAsync(
            FileStorageArea.ChatImages,
            image.StorageKey,
            cancellationToken)
            ?? throw MessageImageNotFound();
        return new(stream, image.ContentType, image.FileSize);
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
        return await transaction.ExecuteAsync<ConversationReadDto>(async ct =>
        {
            var access = await LoadAccessAsync(
                conversationId,
                userId,
                ct,
                tracked: true);
            var previous = access.Participant.LastReadAtUtc;
            var unreadCount = await dbContext.Messages
                .IgnoreQueryFilters()
                .LongCountAsync(
                    x => x.ConversationId == conversationId &&
                         x.SenderUserId != userId &&
                         (!previous.HasValue || x.SentAtUtc > previous.Value),
                    ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            using (tenantMutationScope.Begin(access.TenantId))
            {
                access.Participant.MarkRead(now);
                await dbContext.Notifications
                    .Where(x =>
                        x.RecipientUserId == userId &&
                        x.Type == "chat" &&
                        x.TargetType == "conversation" &&
                        x.TargetId == conversationId &&
                        x.ReadAtUtc == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(x => x.ReadAtUtc, now)
                            .SetProperty(x => x.UpdatedAtUtc, now)
                            .SetProperty(x => x.UpdatedByUserId, userId),
                        ct);
                await dbContext.SaveChangesAsync(ct);
            }

            return new(
                unreadCount,
                access.Participant.LastReadAtUtc!.Value,
                userId);
        }, cancellationToken);
    }

    public async Task EnsureParticipantAsync(
        Guid actorUserId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await LoadAccessAsync(
            conversationId,
            RequireActor(actorUserId),
            cancellationToken);
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
                    TrainerImageUrl = dbContext.TrainerProfiles
                        .IgnoreQueryFilters()
                        .Where(x =>
                            x.TenantId == conversation.TenantId &&
                            x.UserId == conversation.TrainerUserId)
                        .Select(x => x.ImageUrl)
                        .FirstOrDefault(),
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
            userId == result.Conversation.MemberUserId
                ? result.TrainerImageUrl
                : null,
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

    private static Task<bool> CanSendAsync(
        ConversationAccess access,
        CancellationToken cancellationToken)
    {
        _ = access;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
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

    private async Task<ChatMessageDto?> FindMessageAsync(
        Guid conversationId,
        Guid senderUserId,
        Guid clientMessageId,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.ConversationId == conversationId &&
                x.SenderUserId == senderUserId &&
                x.ClientMessageId == clientMessageId)
            .SingleOrDefaultAsync(cancellationToken);
        return message is null ? null : Map(message);
    }

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
            message.ImageStorageKey is null
                ? null
                : $"/api/me/conversations/{message.ConversationId}/messages/{message.Id}/image",
            message.SentAtUtc);

    private async Task DeleteImageQuietlyAsync(string storageKey)
    {
        try
        {
            await fileStorage.DeleteAsync(
                FileStorageArea.ChatImages,
                storageKey,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogOrphanDeleteFailure(logger, storageKey, exception);
        }
    }

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

    private static NotFoundException MessageImageNotFound() =>
        new(
            "message_image_not_found",
            "The message image was not found.");

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
