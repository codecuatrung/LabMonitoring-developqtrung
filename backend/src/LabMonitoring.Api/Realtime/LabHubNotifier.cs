using LabMonitoring.Application.Common.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace LabMonitoring.Api.Realtime;

/// <summary>Sends hub events to the right room group via <see cref="IHubContext{LabHub}"/>.</summary>
internal sealed class LabHubNotifier(IHubContext<LabHub> hub) : IRealtimeNotifier
{
    public Task ComputerStatusChangedAsync(ComputerStatusChangedEvent e, CancellationToken ct = default)
        => hub.Clients.Group(LabHub.RoomGroup(e.RoomId)).SendAsync(RealtimeEvents.ComputerStatusChanged, e, ct);

    public Task MetricsUpdatedAsync(MetricsUpdatedEvent e, CancellationToken ct = default)
        => hub.Clients.Group(LabHub.RoomGroup(e.RoomId)).SendAsync(RealtimeEvents.MetricsUpdated, e, ct);

    public Task AlertRaisedAsync(AlertEvent e, CancellationToken ct = default)
        => e.RoomId is { } roomId
            ? hub.Clients.Group(LabHub.RoomGroup(roomId)).SendAsync(RealtimeEvents.NewAlert, e, ct)
            : hub.Clients.All.SendAsync(RealtimeEvents.NewAlert, e, ct);

    public Task CommandResultReceivedAsync(CommandResultEvent e, CancellationToken ct = default)
        => hub.Clients.Group(LabHub.RoomGroup(e.RoomId)).SendAsync(RealtimeEvents.CommandResultReceived, e, ct);

    public Task CommandIssuedAsync(int computerId, object signedCommand, CancellationToken ct = default)
        => hub.Clients.Group(LabHub.ComputerGroup(computerId)).SendAsync(RealtimeEvents.CommandIssued, signedCommand, ct);

    public Task NotificationCreatedAsync(int userId, NotificationCreatedEvent e, CancellationToken ct = default)
        => hub.Clients.Group(LabHub.UserGroup(userId)).SendAsync(RealtimeEvents.NotificationCreated, e, ct);
}
