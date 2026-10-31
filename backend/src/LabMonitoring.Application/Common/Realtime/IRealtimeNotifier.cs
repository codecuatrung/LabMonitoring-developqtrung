namespace LabMonitoring.Application.Common.Realtime;

/// <summary>
/// Pushes realtime updates to the SignalR hub without the application/infrastructure layers
/// depending on SignalR. Delivery is best-effort (fire-and-forget from callers' perspective).
/// </summary>
public interface IRealtimeNotifier
{
    Task ComputerStatusChangedAsync(ComputerStatusChangedEvent e, CancellationToken ct = default);
    Task MetricsUpdatedAsync(MetricsUpdatedEvent e, CancellationToken ct = default);
    Task AlertRaisedAsync(AlertEvent e, CancellationToken ct = default);
    Task CommandResultReceivedAsync(CommandResultEvent e, CancellationToken ct = default);

    /// <summary>Push a newly created in-app notification to its owner (SRS FR-7.2).</summary>
    Task NotificationCreatedAsync(int userId, NotificationCreatedEvent e, CancellationToken ct = default);

    /// <summary>
    /// Push a signed command envelope down to one Agent. Room-wide commands call this once per
    /// target machine — each envelope is signed with that machine's own AgentSecret (SRS 8.5), so
    /// there is no single payload that could be broadcast to a whole room at once.
    /// </summary>
    Task CommandIssuedAsync(int computerId, object signedCommand, CancellationToken ct = default);
}
