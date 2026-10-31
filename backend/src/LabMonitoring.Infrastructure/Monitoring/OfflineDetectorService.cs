using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Realtime;
using LabMonitoring.Domain.Common;
using LabMonitoring.Infrastructure.Agent;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Infrastructure.Monitoring;

/// <summary>
/// Marks a computer Offline when its last heartbeat is older than the configured threshold
/// (SRS FR-1.5, default 30s). Emits a status change + alert per newly-offline machine.
/// </summary>
public sealed class OfflineDetectorService(
    IServiceScopeFactory scopeFactory,
    IRealtimeNotifier realtime,
    IOptions<AgentDefaultsOptions> options,
    ILogger<OfflineDetectorService> logger) : BackgroundService
{
    private readonly int _thresholdSeconds = options.Value.OfflineThresholdSeconds;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _thresholdSeconds / 3));
        logger.LogInformation("Offline detector running every {Interval}s (threshold {Threshold}s).",
            interval.TotalSeconds, _thresholdSeconds);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Offline sweep failed");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_thresholdSeconds);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertPublisher>();

        var stale = await db.ComputerStatuses
            .Where(s => s.CurrentStatus != ComputerState.Offline
                        && (s.LastHeartbeatAt == null || s.LastHeartbeatAt < cutoff))
            .Select(s => new { s.ComputerId, s.Computer.RoomId, s.Computer.Name })
            .ToListAsync(ct);

        if (stale.Count == 0)
            return;

        var ids = stale.Select(s => s.ComputerId).ToArray();
        await db.ComputerStatuses
            .Where(s => ids.Contains(s.ComputerId))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.CurrentStatus, ComputerState.Offline), ct);

        logger.LogInformation("Marked {Count} computer(s) Offline.", stale.Count);

        foreach (var s in stale)
        {
            await realtime.ComputerStatusChangedAsync(
                new ComputerStatusChangedEvent(s.ComputerId, s.RoomId, ComputerState.Offline.ToString(), null, null), ct);
            await alerts.RaiseAsync(
                s.RoomId, s.ComputerId, NotificationSeverity.Warning,
                "Máy mất kết nối", $"{s.Name} không gửi heartbeat quá {_thresholdSeconds}s.", ct);
        }
    }
}
