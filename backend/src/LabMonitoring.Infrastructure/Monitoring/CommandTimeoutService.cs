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
/// Marks a command Timeout when it has no terminal result (Executed/Failed) within the configured
/// window (SRS FR-4.5, default 60s) — e.g. the Agent went offline mid-command.
/// </summary>
public sealed class CommandTimeoutService(
    IServiceScopeFactory scopeFactory,
    IRealtimeNotifier realtime,
    IOptions<AgentDefaultsOptions> options,
    ILogger<CommandTimeoutService> logger) : BackgroundService
{
    private readonly int _timeoutSeconds = options.Value.CommandTimeoutSeconds;
    private readonly int _agentUpdateTimeoutSeconds = options.Value.AgentUpdateTimeoutSeconds;
    private readonly int _fileTransferTimeoutSeconds = options.Value.FileTransferTimeoutSeconds;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _timeoutSeconds / 3));
        logger.LogInformation("Command timeout sweep running every {Interval}s (timeout {Timeout}s).",
            interval.TotalSeconds, _timeoutSeconds);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Command timeout sweep failed");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_timeoutSeconds);
        // UpdateAgent legitimately takes far longer than every other command kind (download + verify
        // + service restart, result reported late by the process that comes back up) — see
        // AgentDefaultsOptions.AgentUpdateTimeoutSeconds.
        var updateCutoff = DateTime.UtcNow.AddSeconds(-_agentUpdateTimeoutSeconds);
        var transferCutoff = DateTime.UtcNow.AddSeconds(-_fileTransferTimeoutSeconds);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        var stale = await db.CommandResults
            .Where(r => (r.Status == CommandResultStatus.Sent || r.Status == CommandResultStatus.Acknowledged)
                        && ((r.Command.CommandType == Domain.Common.CommandKind.UpdateAgent && r.Command.IssuedAt < updateCutoff)
                            || ((r.Command.CommandType == Domain.Common.CommandKind.PushFile || r.Command.CommandType == Domain.Common.CommandKind.CollectFiles)
                                && r.Command.IssuedAt < transferCutoff)
                            || (r.Command.CommandType != Domain.Common.CommandKind.UpdateAgent
                                && r.Command.CommandType != Domain.Common.CommandKind.PushFile
                                && r.Command.CommandType != Domain.Common.CommandKind.CollectFiles
                                && r.Command.IssuedAt < cutoff)))
            .Select(r => new { r.Id, r.CommandId, r.ComputerId, RoomId = r.Computer.RoomId })
            .ToListAsync(ct);

        if (stale.Count == 0)
            return;

        var ids = stale.Select(s => s.Id).ToArray();
        await db.CommandResults.Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, CommandResultStatus.Timeout), ct);

        logger.LogWarning("Marked {Count} command result(s) Timeout.", stale.Count);

        foreach (var s in stale)
        {
            await realtime.CommandResultReceivedAsync(new CommandResultEvent(
                s.CommandId, s.ComputerId, s.RoomId, CommandResultStatus.Timeout.ToString(),
                "No response from the Agent within the timeout window.", null), ct);
        }
    }
}
