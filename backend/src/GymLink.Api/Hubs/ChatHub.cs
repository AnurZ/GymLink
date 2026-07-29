using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GymLink.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GymLink.Api.Hubs;

[Authorize]
public sealed class ChatHub(
    IChatActorService chatService,
    ChatDeliveryService delivery) : Hub
{
    [HubMethodName("conversation:join")]
    public async Task JoinConversation(Guid conversationId)
    {
        var userId = RequireUser();
        await chatService.EnsureParticipantAsync(
            userId,
            conversationId,
            Context.ConnectionAborted);
        delivery.Join(conversationId, Context.ConnectionId, userId);
    }

    [HubMethodName("conversation:leave")]
    public void LeaveConversation(Guid conversationId) =>
        delivery.Leave(conversationId, Context.ConnectionId);

    [HubMethodName("message:send")]
    public async Task SendMessage(
        Guid conversationId,
        Guid clientMessageId,
        string text)
    {
        var message = await chatService.SendAsync(
            RequireUser(),
            conversationId,
            new SendMessageRequest
            {
                ClientMessageId = clientMessageId,
                Text = text,
            },
            Context.ConnectionAborted);
        await delivery.DeliverAsync(
            conversationId,
            "message:new",
            new { conversationId, message },
            Context.ConnectionAborted);
    }

    [HubMethodName("conversation:read")]
    public async Task MarkRead(Guid conversationId)
    {
        await chatService.EnsureParticipantAsync(
            RequireUser(),
            conversationId,
            Context.ConnectionAborted);
        var result = await chatService.MarkReadAsync(
            RequireUser(),
            conversationId,
            Context.ConnectionAborted);
        await delivery.DeliverAsync(
            conversationId,
            "conversation:read",
            new { conversationId, result.ReadAtUtc },
            Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        delivery.Disconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private Guid RequireUser() =>
        Guid.TryParse(
            Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            ? userId
            : throw new HubException("Authentication is required.");
}
