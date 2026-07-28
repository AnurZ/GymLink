using GymLink.Application.Common;
using GymLink.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/notifications")]
public sealed class NotificationController(
    INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> Search(
        [FromQuery] NotificationSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await notificationService.SearchMineAsync(request, cancellationToken));

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadNotificationCountDto>> UnreadCount(
        CancellationToken cancellationToken) =>
        Ok(await notificationService.GetUnreadCountAsync(cancellationToken));

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<NotificationDto>> MarkRead(
        Guid id,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken) =>
        Ok(await notificationService.MarkReadAsync(id, request, cancellationToken));

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await notificationService.MarkAllReadAsync(cancellationToken);
        return NoContent();
    }
}
