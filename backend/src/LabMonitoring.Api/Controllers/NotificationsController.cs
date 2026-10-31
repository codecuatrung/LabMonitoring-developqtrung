using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>The caller's own in-app notifications (SRS FR-7.2).</summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = AuthPolicies.AdminConsole)]
public sealed class NotificationsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(
        [FromQuery] bool unreadOnly, [FromQuery] int limit, CancellationToken ct)
        => Ok(await sender.Send(new GetMyNotificationsQuery(unreadOnly, limit == 0 ? 50 : limit), ct));

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken ct)
        => Ok(await sender.Send(new GetUnreadNotificationCountQuery(), ct));

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        await sender.Send(new MarkNotificationReadCommand(id), ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await sender.Send(new MarkAllNotificationsReadCommand(), ct);
        return NoContent();
    }
}
