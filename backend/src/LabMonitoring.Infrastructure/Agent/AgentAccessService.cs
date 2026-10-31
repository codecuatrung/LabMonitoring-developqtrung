using LabMonitoring.Application.Agent;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Transfers;
using LabMonitoring.Application.Common.Realtime;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Policies;
using LabMonitoring.Contracts.Agent;
using LabMonitoring.Infrastructure.Notifications;
using LabMonitoring.Infrastructure.Persistence;
using LabMonitoring.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Infrastructure.Agent;

internal sealed class AgentAccessService(
    LabMonitoringDbContext db,
    IOptions<AgentDefaultsOptions> defaults,
    IOptions<AlertOptions> alertOptions,
    IRealtimeNotifier realtime,
    IAlertPublisher alerts,
    AgentSecretProtector secretProtector,
    IAgentPackageStore packageStore,
    IFileTransferStore transferStore,
    IFileTransferPolicy transferPolicy,
    ILogger<AgentAccessService> logger) : IAgentAccessService
{
    private readonly AgentDefaultsOptions _defaults = defaults.Value;
    private readonly AlertOptions _alertOptions = alertOptions.Value;

    public async Task<AgentRegisterResponse> RegisterAsync(AgentRegisterRequest request, CancellationToken ct = default)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == request.RoomCode, ct)
            ?? throw new AgentRegistrationException($"Unknown room code '{request.RoomCode}'.", isConflict: false);

        if (await db.Computers.AnyAsync(c => c.MachineId == request.MachineId, ct))
            throw new AgentRegistrationException($"Machine {request.MachineId} is already registered.", isConflict: true);

        var token = SecretHasher.NewSecret();
        var secret = SecretHasher.NewSecret();

        var computer = new Computer
        {
            RoomId = room.Id,
            Name = DeriveName(request.Hostname),
            MachineId = request.MachineId,
            MacAddress = request.MacAddress,
            AgentVersion = request.AgentVersion,
            AgentTokenHash = SecretHasher.Hash(token),
            // Encrypted (reversible), not hashed — the server signs commands with this secret
            // later (SRS 8.5 "ký số phía server"), so it must be able to recover it. See ADR-0009.
            AgentSecretHash = secretProtector.Protect(secret),
            IsActive = true,
            IsApproved = false,
            Spec = new ComputerSpec
            {
                CpuModel = request.CpuModel,
                CpuCores = request.CpuCores,
                RamTotalMB = request.RamTotalMB,
                DiskTotalGB = request.DiskTotalGB,
                OsVersion = request.OsVersion,
                UpdatedAt = DateTime.UtcNow,
            },
            Status = new ComputerStatus { CurrentStatus = ComputerState.Offline },
        };

        db.Computers.Add(computer);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Agent registered: machine {MachineId} -> computer {ComputerId} in room {RoomCode} (pending approval)",
            request.MachineId, computer.Id, room.Code);

        return new AgentRegisterResponse
        {
            ComputerId = computer.Id,
            ComputerName = computer.Name,
            AgentToken = token,
            AgentSecret = secret,
            Approved = false,
            HeartbeatSeconds = _defaults.HeartbeatSeconds,
        };
    }

    public async Task<AgentPrincipal?> AuthenticateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var hash = SecretHasher.Hash(token);
        var computer = await db.Computers
            .Where(c => c.AgentTokenHash == hash && c.IsActive)
            .Select(c => new { c.Id, c.MachineId, c.RoomId, RoomCode = c.Room.Code, c.IsApproved })
            .FirstOrDefaultAsync(ct);

        return computer is null
            ? null
            : new AgentPrincipal(computer.Id, computer.MachineId, computer.RoomId, computer.RoomCode, computer.IsApproved);
    }

    public async Task<AgentConfigResponse> GetConfigAsync(AgentPrincipal agent, CancellationToken ct = default)
    {
        // Room-scoped policies plus system-wide policies (RoomId == null).
        var appPolicies = await db.ApplicationPolicies
            .Where(p => p.RoomId == null || p.RoomId == agent.RoomId)
            .Select(p => new { p.PolicyType, p.Application.ExecutablePath })
            .ToListAsync(ct);

        var urlPolicies = await db.WebsitePolicies
            .Where(p => p.RoomId == null || p.RoomId == agent.RoomId)
            .Select(p => new { p.PolicyType, p.Domain })
            .ToListAsync(ct);

        var transferFolders = await db.TransferFolders
            .Where(f => f.IsActive && (f.RoomId == null || f.RoomId == agent.RoomId))
            .Select(f => new { f.Id, f.Path, f.Direction })
            .ToListAsync(ct);

        return new AgentConfigResponse
        {
            TransferFolders = transferFolders.Select(f => new Contracts.Agent.TransferFolderDto(f.Id, f.Path, f.Direction.ToString())).ToList(),
            HeartbeatSeconds = _defaults.HeartbeatSeconds,
            OfflineThresholdSeconds = _defaults.OfflineThresholdSeconds,
            ScreenshotIntervalSeconds = _defaults.ScreenshotIntervalSeconds,
            Approved = agent.IsApproved,
            AllowedApplications = appPolicies.Where(p => p.PolicyType == PolicyType.Allow).Select(p => p.ExecutablePath).ToList(),
            BlockedApplications = appPolicies.Where(p => p.PolicyType == PolicyType.Block).Select(p => p.ExecutablePath).ToList(),
            AllowedUrls = urlPolicies.Where(p => p.PolicyType == PolicyType.Allow).Select(p => p.Domain).ToList(),
            BlockedUrls = urlPolicies.Where(p => p.PolicyType == PolicyType.Block).Select(p => p.Domain).ToList(),
        };
    }

    public async Task<AgentHeartbeatResponse> RecordHeartbeatAsync(
        AgentPrincipal agent, AgentHeartbeatRequest request, string? clientIp, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var status = await db.ComputerStatuses.FirstOrDefaultAsync(s => s.ComputerId == agent.ComputerId, ct);
        if (status is null)
        {
            status = new ComputerStatus { ComputerId = agent.ComputerId };
            db.ComputerStatuses.Add(status);
        }

        var state = Enum.TryParse<ComputerState>(request.Status, ignoreCase: true, out var parsed)
            ? parsed
            : ComputerState.Online;

        status.CurrentStatus = state;
        status.LastHeartbeatAt = now;
        status.CurrentWindowsUser = request.CurrentWindowsUser;
        // Prefer the Agent's self-reported LAN IP (ADR-0014) — behind Docker Desktop's NAT (dev) or
        // any reverse proxy (prod), the raw connection address reflects the last hop, not the real
        // machine. Older agents that don't send it yet still fall back to the connection address.
        status.LastIpAddress = string.IsNullOrWhiteSpace(request.LocalIpAddress) ? clientIp : request.LocalIpAddress;

        db.ComputerMetrics.Add(new ComputerMetric
        {
            ComputerId = agent.ComputerId,
            RecordedAt = request.RecordedAt.UtcDateTime,
            CpuUsagePercent = request.CpuUsagePercent,
            RamUsagePercent = request.RamUsagePercent,
            DiskUsagePercent = request.DiskUsagePercent,
            TemperatureC = request.TemperatureC,
        });

        if (request.AgentVersion is not null)
        {
            await db.Computers.Where(c => c.Id == agent.ComputerId && c.AgentVersion != request.AgentVersion)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.AgentVersion, request.AgentVersion), ct);
        }

        await TrackSessionLoginAsync(agent.ComputerId, agent.RoomId, request.CurrentWindowsUser, now, ct);
        await CheckResourceAlertsAsync(agent, status, request.CpuUsagePercent, request.RamUsagePercent, ct);

        await db.SaveChangesAsync(ct);

        await realtime.ComputerStatusChangedAsync(new ComputerStatusChangedEvent(
            agent.ComputerId, agent.RoomId, state.ToString(), request.CurrentWindowsUser, now), ct);
        await realtime.MetricsUpdatedAsync(new MetricsUpdatedEvent(
            agent.ComputerId, agent.RoomId, request.CpuUsagePercent, request.RamUsagePercent,
            request.DiskUsagePercent, request.TemperatureC, now), ct);

        return new AgentHeartbeatResponse
        {
            Acknowledged = true,
            ServerTimeUtc = now,
            HeartbeatSeconds = _defaults.HeartbeatSeconds,
        };
    }

    public async Task RecordCommandResultAsync(AgentPrincipal agent, AgentCommandResultRequest request, CancellationToken ct = default)
    {
        var result = await db.CommandResults
            .FirstOrDefaultAsync(r => r.CommandId == request.CommandId && r.ComputerId == agent.ComputerId, ct);
        if (result is null)
        {
            logger.LogWarning("Command result for unknown command {CommandId} on computer {ComputerId} ignored.",
                request.CommandId, agent.ComputerId);
            return;
        }

        result.Status = request.Status switch
        {
            CommandExecutionStatus.Executed => Domain.Common.CommandResultStatus.Executed,
            CommandExecutionStatus.Failed => Domain.Common.CommandResultStatus.Failed,
            CommandExecutionStatus.Timeout => Domain.Common.CommandResultStatus.Timeout,
            _ => Domain.Common.CommandResultStatus.Acknowledged,
        };
        result.ExecutedAt = DateTime.UtcNow;
        result.ErrorMessage = request.ErrorMessage;
        await db.SaveChangesAsync(ct);

        await realtime.CommandResultReceivedAsync(new CommandResultEvent(
            request.CommandId, agent.ComputerId, agent.RoomId, result.Status.ToString(), result.ErrorMessage, result.ExecutedAt), ct);
    }

    public async Task<Stream?> OpenLatestUpdatePackageAsync(CancellationToken ct = default)
    {
        var fileName = await db.AgentPackages
            .OrderByDescending(p => p.UploadedAt)
            .Select(p => p.FileName)
            .FirstOrDefaultAsync(ct);

        return fileName is null ? null : await packageStore.OpenReadAsync(fileName, ct);
    }

    public async Task<(Stream Content, string FileName)> OpenTransferFileAsync(AgentPrincipal agent, int transferId, CancellationToken ct = default)
    {
        var transfer = await db.FileTransfers.FirstOrDefaultAsync(t => t.Id == transferId && t.Direction == TransferDirection.Push, ct)
            ?? throw new NotFoundException(nameof(FileTransfer), transferId);
        await EnsureTransferTargetAsync(agent, transfer, ct);
        return (transferStore.OpenPushFile(transfer.Id), transfer.FileName ?? "file.bin");
    }

    public async Task SaveCollectedAsync(AgentPrincipal agent, int transferId, Stream content, int fileCount, bool truncated, CancellationToken ct = default)
    {
        var transfer = await db.FileTransfers.FirstOrDefaultAsync(t => t.Id == transferId && t.Direction == TransferDirection.Collect, ct)
            ?? throw new NotFoundException(nameof(FileTransfer), transferId);
        await EnsureTransferTargetAsync(agent, transfer, ct);

        var maxBytes = transfer.MaxBytes > 0 ? transfer.MaxBytes : transferPolicy.MaxCollectBytes;
        var (sha256, size) = await transferStore.SaveCollectedAsync(transfer.Id, agent.ComputerId, content, maxBytes, ct);

        var existing = await db.CollectedFiles.FirstOrDefaultAsync(c => c.FileTransferId == transfer.Id && c.ComputerId == agent.ComputerId, ct);
        if (existing is null)
        {
            existing = new CollectedFile { FileTransferId = transfer.Id, ComputerId = agent.ComputerId };
            db.CollectedFiles.Add(existing);
        }
        existing.Sha256 = sha256;
        existing.SizeBytes = size;
        existing.FileCount = fileCount;
        existing.Truncated = truncated;
        existing.ReceivedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Collected {Files} file(s), {Size} bytes from computer {ComputerId} for transfer {TransferId}",
            fileCount, size, agent.ComputerId, transfer.Id);
    }

    /// <summary>An Agent may only touch a transfer it is one of the targets of (its CommandResult row exists).</summary>
    private async Task EnsureTransferTargetAsync(AgentPrincipal agent, FileTransfer transfer, CancellationToken ct)
    {
        var isTarget = transfer.CommandId is { } commandId
            && await db.CommandResults.AnyAsync(r => r.CommandId == commandId && r.ComputerId == agent.ComputerId, ct);
        if (!isTarget)
            throw new ForbiddenException("This machine is not a target of this transfer.");
    }

    /// <summary>
    /// CPU/RAM threshold alert (SRS FR-7.1). Raises once per breach "episode" — the *AlertRaisedAt
    /// timestamp on ComputerStatus is set on the first breaching heartbeat and cleared once the
    /// metric recovers, so a sustained breach doesn't re-alert every heartbeat but a new breach
    /// after recovery does.
    /// </summary>
    private async Task CheckResourceAlertsAsync(
        AgentPrincipal agent, ComputerStatus status, decimal? cpu, decimal? ram, CancellationToken ct)
    {
        await CheckOneAsync(cpu, _alertOptions.CpuThresholdPercent, "CPU",
            () => status.CpuAlertRaisedAt, v => status.CpuAlertRaisedAt = v);
        await CheckOneAsync(ram, _alertOptions.RamThresholdPercent, "RAM",
            () => status.RamAlertRaisedAt, v => status.RamAlertRaisedAt = v);

        async Task CheckOneAsync(decimal? value, decimal threshold, string label,
            Func<DateTime?> getRaisedAt, Action<DateTime?> setRaisedAt)
        {
            if (value is null)
                return;

            if (value >= threshold)
            {
                if (getRaisedAt() is not null)
                    return; // already alerted for this breach; wait for recovery before re-alerting

                setRaisedAt(DateTime.UtcNow);
                var name = await db.Computers.Where(c => c.Id == agent.ComputerId).Select(c => c.Name).FirstOrDefaultAsync(ct);
                await alerts.RaiseAsync(agent.RoomId, agent.ComputerId, NotificationSeverity.Warning,
                    $"{label} vượt ngưỡng", $"{name} có {label} = {value:0.#}% (ngưỡng {threshold:0.#}%).", ct);
            }
            else
            {
                setRaisedAt(null);
            }
        }
    }

    /// <summary>
    /// Links the Windows login on this machine to the active Session's SessionComputers row
    /// (SRS FR-2.1/2.2): first heartbeat with a user present → LoginAt; first heartbeat with no
    /// user after a login → LogoutAt. No-op outside an Active session for this room.
    /// </summary>
    private async Task TrackSessionLoginAsync(int computerId, int roomId, string? windowsUser, DateTime now, CancellationToken ct)
    {
        var sessionComputer = await db.SessionComputers
            .Where(sc => sc.ComputerId == computerId
                         && sc.Session.RoomId == roomId
                         && sc.Session.Status == Domain.Common.SessionStatus.Active)
            .FirstOrDefaultAsync(ct);

        if (sessionComputer is null)
            return;

        if (!string.IsNullOrWhiteSpace(windowsUser) && sessionComputer.LoginAt is null)
        {
            sessionComputer.LoginAt = now;
        }
        else if (string.IsNullOrWhiteSpace(windowsUser) && sessionComputer.LoginAt is not null && sessionComputer.LogoutAt is null)
        {
            sessionComputer.LogoutAt = now;
        }
    }

    private static string DeriveName(string hostname)
        => string.IsNullOrWhiteSpace(hostname) ? "PC" : hostname.Trim().ToUpperInvariant()[..Math.Min(50, hostname.Trim().Length)];
}
