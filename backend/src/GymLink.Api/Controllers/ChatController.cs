using GymLink.Api.Hubs;
using GymLink.Application.Common;
using GymLink.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymLink.Domain.Engagement;

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

    [HttpGet("{conversationId:guid}")]
    public async Task<ActionResult<ConversationDto>> Get(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        Ok(await chatService.GetMineAsync(conversationId, cancellationToken));

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

    [HttpPost("{conversationId:guid}/images")]
    [RequestSizeLimit(Message.MaximumImageFileSizeBytes + 65536)]
    public async Task<ActionResult<ChatMessageDto>> SendImage(
        Guid conversationId,
        [FromForm] ChatImageUploadForm form,
        CancellationToken cancellationToken)
    {
        var upload = await form.ToUploadAsync(cancellationToken);
        var message = await chatService.SendImageAsync(
            conversationId,
            upload,
            cancellationToken);
        await delivery.DeliverAsync(
            conversationId,
            "message:new",
            new { conversationId, message },
            cancellationToken);
        return Ok(message);
    }

    [HttpGet("{conversationId:guid}/messages/{messageId:guid}/image")]
    public async Task<IActionResult> Image(
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var image = await chatService.GetImageAsync(
            conversationId,
            messageId,
            cancellationToken);
        Response.Headers.CacheControl = "private,max-age=31536000,immutable";
        return File(image.Content, image.ContentType, enableRangeProcessing: true);
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
            new
            {
                conversationId,
                result.ReadAtUtc,
                result.ReaderUserId,
            },
            cancellationToken);
        return Ok(result);
    }
}

public sealed class ChatImageUploadForm
{
    public Guid ClientMessageId { get; init; }

    public required IFormFile File { get; init; }

    public async Task<ChatImageUpload> ToUploadAsync(
        CancellationToken cancellationToken)
    {
        if (File.Length > Message.MaximumImageFileSizeBytes)
        {
            throw new BadHttpRequestException("The image must be 5 MiB or smaller.");
        }

        await using var stream = File.OpenReadStream();
        using var buffer = new MemoryStream((int)File.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        return new(
            ClientMessageId,
            buffer.ToArray(),
            File.ContentType,
            File.FileName);
    }
}
