using LabMonitoring.Contracts.Agent;

namespace LabMonitoring.Application.Agent;

/// <summary>Identity of an authenticated Agent, resolved from its AgentToken.</summary>
public sealed record AgentPrincipal(int ComputerId, Guid MachineId, int RoomId, string RoomCode, bool IsApproved);

/// <summary>Backing operations for the Agent-facing endpoints (SRS 8.2).</summary>
public interface IAgentAccessService
{
    /// <summary>Self-registration. Throws <see cref="AgentRegistrationException"/> on a bad room code
    /// or an already-registered machine.</summary>
    Task<AgentRegisterResponse> RegisterAsync(AgentRegisterRequest request, CancellationToken ct = default);

    /// <summary>Validates <c>Authorization: AgentToken &lt;token&gt;</c>; returns null when invalid.</summary>
    Task<AgentPrincipal?> AuthenticateAsync(string token, CancellationToken ct = default);

    Task<AgentConfigResponse> GetConfigAsync(AgentPrincipal agent, CancellationToken ct = default);

    /// <summary>Persists a heartbeat: updates <c>ComputerStatus</c>, appends a <c>ComputerMetric</c>,
    /// and pushes realtime updates to the room (SRS FR-1.1/1.2).</summary>
    Task<AgentHeartbeatResponse> RecordHeartbeatAsync(AgentPrincipal agent, AgentHeartbeatRequest request, string? clientIp, CancellationToken ct = default);

    /// <summary>Records the Agent's final outcome for a dispatched command (SRS FR-4.5, 8.2).</summary>
    Task RecordCommandResultAsync(AgentPrincipal agent, AgentCommandResultRequest request, CancellationToken ct = default);

    /// <summary>Opens the most recently uploaded update package (SRS H1 "auto-update") for download —
    /// null when nothing has been uploaded yet.</summary>
    Task<Stream?> OpenLatestUpdatePackageAsync(CancellationToken ct = default);

    /// <summary>Opens the file of a PushFile transfer for an Agent that is one of its targets
    /// (403 otherwise). The stream is seekable so HTTP Range requests can resume a partial download.</summary>
    Task<(Stream Content, string FileName)> OpenTransferFileAsync(AgentPrincipal agent, int transferId, CancellationToken ct = default);

    /// <summary>Stores the zip an Agent uploaded for a CollectFiles transfer it is a target of.</summary>
    Task SaveCollectedAsync(AgentPrincipal agent, int transferId, Stream content, int fileCount, bool truncated, CancellationToken ct = default);
}

public sealed class AgentRegistrationException(string reason, bool isConflict) : Exception(reason)
{
    public bool IsConflict { get; } = isConflict;
}
