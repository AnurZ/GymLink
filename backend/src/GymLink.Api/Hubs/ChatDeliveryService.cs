using System.Collections.Concurrent;
using GymLink.Application.Common;
using GymLink.Application.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace GymLink.Api.Hubs;

public sealed class ChatDeliveryService(
    IServiceScopeFactory scopeFactory,
    IHubContext<ChatHub> hubContext)
{
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<string, Guid>> subscriptions = new();

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
            catch (Exception exception) when (
                exception is AuthorizationDeniedException or NotFoundException)
            {
                Leave(conversationId, connection.Key);
            }
        }
    }
}
