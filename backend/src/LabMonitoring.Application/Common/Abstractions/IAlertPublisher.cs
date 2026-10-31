using LabMonitoring.Domain.Common;

namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// Raises an alert (SRS FR-7): pushes the existing realtime <c>NewAlert</c> event, persists an
/// in-app <c>Notification</c> row per relevant user (Administrators + any Technician/Teacher scoped
/// to the room), and emails those with an address on file — all best-effort, never throws.
/// </summary>
public interface IAlertPublisher
{
    Task RaiseAsync(
        int? roomId, int? computerId, NotificationSeverity severity, string title, string message,
        CancellationToken ct = default);
}
