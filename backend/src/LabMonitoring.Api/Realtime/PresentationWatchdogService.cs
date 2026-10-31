using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace LabMonitoring.Api.Realtime;

/// <summary>
/// Ends presentations whose admin stopped sending frames (the browser sends a keepalive frame every
/// ~2s, so a long silence means the tab froze/crashed without the hub noticing yet). Notifies the
/// Agents' frame group so their overlays unlock — their own dead-man timers are the second safety net.
/// </summary>
internal sealed class PresentationWatchdogService(
    IPresentationSessionRegistry presentations, IHubContext<LabHub> hub, ILogger<PresentationWatchdogService> logger)
    : BackgroundService
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var session in presentations.EndIdle(IdleLimit))
            {
                logger.LogWarning("Presentation {SessionId} in room {RoomId} ended: no frames for {Idle}s.",
                    session.SessionId, session.RoomId, IdleLimit.TotalSeconds);
                await hub.Clients.Group(LabHub.PresentationGroup(session.SessionId))
                    .SendAsync(RealtimeEvents.PresentationEnded, session.SessionId, stoppingToken);
            }
        }
    }
}
