using GymLink.Api.Hubs;
using GymLink.Application.Common;
using GymLink.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/conversations")]
public sealed class ChatController(
    IChatService chatService,
    ChatDeliveryService delivery) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ConversationDto>> Open(
        OpenConversationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await chatService.OpenAsync(request, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedResult<ConversationDto>>> Search(
        [FromQuery] ConversationSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await chatService.SearchMineAsync(request, cancellationToken));

    [HttpGet("{conversationId:guid}/messages")]
    public async Task<ActionResult<MessageHistoryDto>> Messages(
        Guid conversationId,
        [FromQuery] MessageHistoryRequest request,
        CancellationToken cancellationToken) =>
        Ok(await chatService.GetMessagesAsync(
            conversationId,
            request,
            cancellationToken));

    [HttpPost("{conversationId:guid}/messages")]
    public async Task<ActionResult<ChatMessageDto>> Send(
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var message = await chatService.SendAsync(
            conversationId,
            request,
            cancellationToken);
        await delivery.DeliverAsync(
            conversationId,
            "message:new",
            new { conversationId, message },
            cancellationToken);
        return Ok(message);
    }

    [HttpPost("{conversationId:guid}/read")]
    public async Task<ActionResult<ConversationReadDto>> MarkRead(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var result = await chatService.MarkReadAsync(
            conversationId,
            cancellationToken);
        await delivery.DeliverAsync(
            conversationId,
            "conversation:read",
            new { conversationId, result.ReadAtUtc },
            cancellationToken);
        return Ok(result);
    }
}
