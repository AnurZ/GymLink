using System.Collections.Concurrent;
using GymLink.Application.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace GymLink.Api.Hubs;

public sealed class ChatDeliveryService(
    IServiceScopeFactory scopeFactory,
    IHubContext<ChatHub> hubContext,
    ILogger<ChatDeliveryService> logger) : IConversationRealtimeNotifier
{
    private static readonly Action<ILogger, Guid, string, Exception?>
        AvailabilityDeliveryFailed = LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1, nameof(AvailabilityDeliveryFailed)),
            "Could not deliver conversation {ConversationId} availability to connection {ConnectionId}.");
    private static readonly Action<ILogger, string, Guid, string, Exception?>
        EventDeliveryFailed = LoggerMessage.Define<string, Guid, string>(
            LogLevel.Warning,
            new EventId(2, nameof(EventDeliveryFailed)),
            "Could not deliver {EventName} for conversation {ConversationId} to connection {ConnectionId}.");
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<string, Guid>> subscriptions = new();
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<string, byte>> userConnections = new();
    private readonly ConcurrentDictionary<string, Guid> connectionUsers = new();

    public void Connect(string connectionId, Guid userId)
    {
        connectionUsers[connectionId] = userId;
        userConnections
            .GetOrAdd(userId, _ => new())
            [connectionId] = 0;
    }

    public void Join(Guid conversationId, string connectionId, Guid userId) =>
        subscriptions
            .GetOrAdd(conversationId, _ => new())
            [connectionId] = userId;

    public void Leave(Guid conversationId, string connectionId)
    {
        if (!subscriptions.TryGetValue(conversationId, out var connections))
        {
            return;
        }

        connections.TryRemove(connectionId, out _);
        if (connections.IsEmpty)
        {
            subscriptions.TryRemove(conversationId, out _);
        }
    }

    public void Disconnect(string connectionId)
    {
        foreach (var conversationId in subscriptions.Keys)
        {
            Leave(conversationId, connectionId);
        }

        if (!connectionUsers.TryRemove(connectionId, out var userId) ||
            !userConnections.TryGetValue(userId, out var connections))
        {
            return;
        }

        connections.TryRemove(connectionId, out _);
        if (connections.IsEmpty)
        {
            userConnections.TryRemove(userId, out _);
        }
    }

    public async Task ConversationAvailableAsync(
        ConversationProvisioningResult conversation,
        CancellationToken cancellationToken)
    {
        foreach (var userId in new[]
                 {
                     conversation.MemberUserId,
                     conversation.TrainerUserId,
                 })
        {
            if (!userConnections.TryGetValue(userId, out var connections))
            {
                continue;
            }

            foreach (var connectionId in connections.Keys.ToArray())
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var chatService = scope.ServiceProvider
                        .GetRequiredService<IChatActorService>();
                    await chatService.EnsureParticipantAsync(
                        userId,
                        conversation.ConversationId,
                        cancellationToken);
                    await hubContext.Clients.Client(connectionId).SendAsync(
                        "conversation:available",
                        new { conversationId = conversation.ConversationId },
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    Disconnect(connectionId);
                    AvailabilityDeliveryFailed(
                        logger,
                        conversation.ConversationId,
                        connectionId,
                        exception);
                }
            }
        }
    }

    public async Task DeliverAsync(
        Guid conversationId,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        if (!subscriptions.TryGetValue(conversationId, out var connections))
        {
            return;
        }

        foreach (var connection in connections.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider
                .GetRequiredService<IChatActorService>();
            try
            {
                await chatService.EnsureParticipantAsync(
                    connection.Value,
                    conversationId,
                    cancellationToken);
                await hubContext.Clients.Client(connection.Key)
                    .SendAsync(eventName, payload, cancellationToken);
            }
            catch (Exception exception)
            {
                Leave(conversationId, connection.Key);
                EventDeliveryFailed(
                    logger,
                    eventName,
                    conversationId,
                    connection.Key,
                    exception);
            }
        }
    }
}
