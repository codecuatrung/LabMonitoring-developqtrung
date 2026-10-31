namespace LabMonitoring.Application.Common.Realtime;

/// <summary>Names of the SignalR hub methods pushed to Web Admin clients (SRS 8.4).</summary>
public static class RealtimeEvents
{
    public const string ComputerStatusChanged = "ComputerStatusChanged";
    public const string MetricsUpdated = "MetricsUpdated";
    public const string NewAlert = "NewAlert";
    public const string CommandResultReceived = "CommandResultReceived";
    public const string NotificationCreated = "NotificationCreated";

    // Server -> Agent
    public const string CommandIssued = "CommandIssued";

    // Remote control signaling (ADR-0013) — relayed 1:1 between an admin connection and an Agent
    // connection by LabHub itself, not via IRealtimeNotifier (not a broadcast/cross-cutting push).
    public const string RemoteControlRequested = "RemoteControlRequested";
    public const string RemoteControlOffer = "RemoteControlOffer";
    public const string RemoteControlAnswer = "RemoteControlAnswer";
    public const string RemoteControlIceCandidate = "RemoteControlIceCandidate";
    public const string RemoteControlEnded = "RemoteControlEnded";
    public const string PresentationFrame = "PresentationFrame";
    public const string PresentationEnded = "PresentationEnded";
}

/// <summary>Payload for <see cref="RealtimeEvents.ComputerStatusChanged"/>.</summary>
public sealed record ComputerStatusChangedEvent(
    int ComputerId,
    int RoomId,
    string Status,
    string? CurrentWindowsUser,
    DateTime? LastHeartbeatAt);

/// <summary>Payload for <see cref="RealtimeEvents.MetricsUpdated"/>.</summary>
public sealed record MetricsUpdatedEvent(
    int ComputerId,
    int RoomId,
    decimal? CpuUsagePercent,
    decimal? RamUsagePercent,
    decimal? DiskUsagePercent,
    decimal? TemperatureC,
    DateTime RecordedAt);

/// <summary>Payload for <see cref="RealtimeEvents.NewAlert"/>.</summary>
public sealed record AlertEvent(
    int? RoomId,
    int? ComputerId,
    string Severity,
    string Title,
    string Message,
    DateTime CreatedAt);

/// <summary>Payload for <see cref="RealtimeEvents.CommandResultReceived"/>.</summary>
public sealed record CommandResultEvent(
    long CommandId,
    int ComputerId,
    int RoomId,
    string Status,
    string? ErrorMessage,
    DateTime? ExecutedAt);

/// <summary>Payload for <see cref="RealtimeEvents.NotificationCreated"/> (SRS FR-7.2).</summary>
public sealed record NotificationCreatedEvent(
    long Id,
    string Title,
    string Message,
    string Severity,
    DateTime CreatedAt);
