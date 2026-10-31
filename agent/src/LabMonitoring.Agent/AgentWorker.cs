using System.Text.Json;
using LabMonitoring.Agent.Client;
using LabMonitoring.Agent.Configuration;
using LabMonitoring.Agent.Monitoring;
using LabMonitoring.Agent.Outbox;
using LabMonitoring.Agent.Protocol;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Agent;

/// <summary>
/// Root loop of the LabAgent service: ensure registered → connect the hub → send a heartbeat every
/// N seconds, queuing locally and retrying with backoff while the server is unreachable
/// (SRS FR-1.2, NFR "Độ tin cậy Agent").
/// </summary>
public sealed class AgentWorker(
    IOptions<AgentOptions> options,
    AgentRegistrationService registration,
    AgentApiClient api,
    AgentHubClient hub,
    SystemMetricsCollector metrics,
    SessionInfoProvider sessionInfo,
    HeartbeatOutbox outbox,
    AgentConfigCache configCache,
    AgentPaths paths,
    ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ConfigRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AgentOptions _options = options.Value;
    private int _heartbeatSeconds;
    private DateTime _lastConfigRefreshUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _heartbeatSeconds = _options.HeartbeatSeconds;
        logger.LogInformation("LabAgent starting. Server={Server}", _options.ServerBaseUrl);

        await ReportPendingUpdateResultAsync(stoppingToken);
        await WaitForRegistrationAsync(stoppingToken);
        await TryRefreshConfigAsync(stoppingToken);
        await TryStartHubAsync(stoppingToken);

        // Prime the CPU sampler.
        metrics.Collect();

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(_heartbeatSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }

            // Also checked here, not just once at startup: the Updater (SRS H1) doesn't write the
            // result file until AFTER its ~15s post-restart health check, which routinely lands well
            // after this process's own startup sequence has already finished — the file genuinely
            // doesn't exist yet at the one-time startup check. Cheap no-op once it's been reported
            // (the file is deleted after reporting).
            await ReportPendingUpdateResultAsync(stoppingToken);

            var heartbeat = BuildHeartbeat();
            var accepted = await SendAsync(heartbeat, stoppingToken);

            if (accepted)
            {
                await outbox.DrainAsync(h => SendAsync(h, stoppingToken), stoppingToken);
                if (hub.State != Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
                    await TryStartHubAsync(stoppingToken);
                if (DateTime.UtcNow - _lastConfigRefreshUtc >= ConfigRefreshInterval)
                    await TryRefreshConfigAsync(stoppingToken);
            }
            else
            {
                outbox.Enqueue(heartbeat);
            }
        }

        await hub.DisposeAsync();
        logger.LogInformation("LabAgent stopped. {Queued} heartbeat(s) left in the outbox.", outbox.Count);
    }

    /// <summary>
    /// If this process just came up after an UpdateAgent-triggered restart (SRS H1 "auto-update"),
    /// <c>LabMonitoring.Agent.Updater</c> left a marker recording the outcome — report it now, using
    /// the exact same <see cref="AgentCommandResultRequest"/>/<see cref="AgentApiClient.SendCommandResultAsync"/>
    /// every other command already uses. This is the ONLY place that result is ever reported: the
    /// process that ran <c>AgentUpdateExecutor</c> deferred it (see <see cref="CommandExecutionResult.Deferred"/>)
    /// precisely because it can't know the outcome — only the process that comes back up can.
    /// </summary>
    private async Task ReportPendingUpdateResultAsync(CancellationToken ct)
    {
        var resultFile = Path.Combine(paths.DataDirectory, "pending-command-result.json");
        if (!File.Exists(resultFile))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(resultFile, ct);
            var pending = JsonSerializer.Deserialize<PendingUpdateResult>(json, JsonOpts);
            if (pending is not null)
            {
                var status = pending.Status.Equals("Executed", StringComparison.OrdinalIgnoreCase)
                    ? Protocol.CommandExecutionStatus.Executed
                    : Protocol.CommandExecutionStatus.Failed;

                await api.SendCommandResultAsync(new AgentCommandResultRequest
                {
                    CommandId = pending.CommandId,
                    Status = status,
                    ErrorMessage = pending.ErrorMessage,
                }, ct);

                logger.LogInformation("Reported deferred UpdateAgent result for command {CommandId}: {Status}", pending.CommandId, status);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read/report pending update result from {File}", resultFile);
        }
        finally
        {
            try { File.Delete(resultFile); } catch (Exception ex) { logger.LogDebug(ex, "Failed to delete {File} (harmless).", resultFile); }
        }
    }

    private sealed record PendingUpdateResult(long CommandId, string Status, string? ErrorMessage);

    private async Task WaitForRegistrationAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await registration.EnsureRegisteredAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Registration not complete; retrying in {Backoff}s", backoff.TotalSeconds);
                await Task.Delay(backoff, ct);
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 300));
            }
        }
    }

    private async Task TryRefreshConfigAsync(CancellationToken ct)
    {
        try
        {
            var config = await api.GetConfigAsync(ct);
            if (config is not null)
            {
                configCache.Update(config);
                _lastConfigRefreshUtc = DateTime.UtcNow;

                if (config.HeartbeatSeconds > 0)
                    _heartbeatSeconds = config.HeartbeatSeconds;

                logger.LogInformation(
                    "Config: heartbeat={Heartbeat}s, approved={Approved}, allowApps={Apps}, allowUrls={Urls}",
                    config.HeartbeatSeconds, config.Approved, config.AllowedApplications.Count, config.AllowedUrls.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Config refresh failed; keeping heartbeat={Heartbeat}s and the last synced whitelist", _heartbeatSeconds);
        }
    }

    private async Task TryStartHubAsync(CancellationToken ct)
    {
        try
        {
            await hub.StartAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hub connection failed; will retry.");
        }
    }

    private AgentHeartbeatRequest BuildHeartbeat()
    {
        var sample = metrics.Collect();
        return new AgentHeartbeatRequest
        {
            Status = "Online",
            CurrentWindowsUser = sessionInfo.CurrentConsoleUser(),
            AgentVersion = AgentRegistrationService.AgentVersion,
            LocalIpAddress = ResolveLocalIpAddress(),
            CpuUsagePercent = sample.CpuUsagePercent,
            RamUsagePercent = sample.RamUsagePercent,
            DiskUsagePercent = sample.DiskUsagePercent,
            TemperatureC = sample.TemperatureC,
            RecordedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// The machine's own primary LAN IPv4 (ADR-0014) — self-reported because the server-side
    /// connection's remote address isn't reliable behind Docker/NAT or a reverse proxy. A UDP
    /// "connect" never actually sends a packet (UDP is connectionless); it only asks the OS to pick
    /// the outbound route/interface for that destination, which is enough to read back the local
    /// address without needing 8.8.8.8 to be reachable.
    /// </summary>
    private static string? ResolveLocalIpAddress()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as System.Net.IPEndPoint)?.Address.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> SendAsync(AgentHeartbeatRequest heartbeat, CancellationToken ct)
    {
        try
        {
            var response = await api.SendHeartbeatAsync(heartbeat, ct);
            if (response is { Acknowledged: true })
            {
                if (response.HeartbeatSeconds > 0 && response.HeartbeatSeconds != _heartbeatSeconds)
                {
                    logger.LogInformation("Server changed heartbeat interval to {Heartbeat}s", response.HeartbeatSeconds);
                    _heartbeatSeconds = response.HeartbeatSeconds;
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Heartbeat send failed");
        }

        return false;
    }
}
