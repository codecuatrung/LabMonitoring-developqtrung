using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Realtime;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Common;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Infrastructure.Notifications;

/// <summary>
/// Fans an alert out to every channel (SRS FR-7): realtime <c>NewAlert</c> (unchanged, room-wide),
/// a persisted <c>Notification</c> per relevant user, and email for those with an address on file.
/// Relevant users = every active Administrator, plus (when the alert is room-scoped) every active
/// Technician/Teacher with a <c>UserRoomScope</c> on that room.
/// </summary>
internal sealed class AlertPublisher(
    LabMonitoringDbContext db,
    IRealtimeNotifier realtime,
    IEmailSender email,
    ILogger<AlertPublisher> logger) : IAlertPublisher
{
    public async Task RaiseAsync(
        int? roomId, int? computerId, NotificationSeverity severity, string title, string message,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Realtime room/global broadcast — unchanged behavior from before M5.
        await realtime.AlertRaisedAsync(new AlertEvent(roomId, computerId, severity.ToString(), title, message, now), ct);

        var recipients = await db.Users
            .Where(u => u.IsActive && (
                u.UserRoles.Any(ur => ur.RoleId == SeededRoles.AdministratorId)
                || (roomId != null && u.RoomScopes.Any(s => s.RoomId == roomId))))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        if (recipients.Count == 0)
            return;

        var notifications = recipients.Select(r => new Notification
        {
            UserId = r.Id,
            Title = title,
            Message = message,
            Severity = severity,
            CreatedAt = now,
        }).ToList();

        // Persisting the Notification rows is genuinely best-effort — callers include
        // RecordHeartbeatAsync, where this must never fail the heartbeat itself (e.g. a recipient
        // deactivated/removed between being selected just above and this insert running — a real
        // FK violation seen under concurrent user-management writes, not a hypothetical). On
        // failure we must also stop EF Core from retrying these same rows: `db` is the SAME scoped
        // DbContext the caller uses for its own pending changes (ComputerStatus/ComputerMetric/…),
        // and if we left these Added-but-unsaved entities tracked, the caller's own later
        // SaveChangesAsync would try to flush them too and fail with the identical error.
        db.Notifications.AddRange(notifications);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist alert '{Title}' (room={RoomId}, computer={ComputerId})", title, roomId, computerId);
            db.Notifications.RemoveRange(notifications);
            return;
        }

        foreach (var (recipient, notification) in recipients.Zip(notifications))
        {
            try
            {
                await realtime.NotificationCreatedAsync(recipient.Id,
                    new NotificationCreatedEvent(notification.Id, title, message, severity.ToString(), now), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push realtime notification to user {UserId}", recipient.Id);
            }

            if (!string.IsNullOrWhiteSpace(recipient.Email))
                await email.SendAsync(recipient.Email, title, message, ct);
        }
    }
}
