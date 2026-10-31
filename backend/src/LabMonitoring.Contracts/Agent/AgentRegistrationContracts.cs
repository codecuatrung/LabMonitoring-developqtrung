namespace LabMonitoring.Contracts.Agent;

/// <summary>Payload for <c>POST /api/v1/agent/register</c> (SRS 8.2). Sent once, unauthenticated,
/// gated by the room registration code.</summary>
public sealed record AgentRegisterRequest
{
    public required Guid MachineId { get; init; }

    /// <summary>Room code the technician entered at install time (matches <c>Rooms.Code</c>).</summary>
    public required string RoomCode { get; init; }

    public required string Hostname { get; init; }
    public string? MacAddress { get; init; }
    public string? AgentVersion { get; init; }

    // Hardware inventory (best effort)
    public string? CpuModel { get; init; }
    public int? CpuCores { get; init; }
    public int? RamTotalMB { get; init; }
    public int? DiskTotalGB { get; init; }
    public string? OsVersion { get; init; }
}

/// <summary>Response of <c>POST /api/v1/agent/register</c>. The plaintext token and secret are
/// returned only here — the server stores hashes only.</summary>
public sealed record AgentRegisterResponse
{
    public required int ComputerId { get; init; }
    public required string ComputerName { get; init; }

    /// <summary>Bearer value for <c>Authorization: AgentToken &lt;token&gt;</c> on every later request.</summary>
    public required string AgentToken { get; init; }

    /// <summary>Per-machine HMAC key used to verify signed control commands (SRS 8.5). Store with DPAPI.</summary>
    public required string AgentSecret { get; init; }

    public required bool Approved { get; init; }
    public required int HeartbeatSeconds { get; init; }
}

/// <summary>Response of <c>GET /api/v1/agent/config</c> (SRS 8.2).</summary>
public sealed record AgentConfigResponse
{
    public required int HeartbeatSeconds { get; init; }
    public required int OfflineThresholdSeconds { get; init; }
    public required int ScreenshotIntervalSeconds { get; init; }
    public required bool Approved { get; init; }

    public required IReadOnlyList<string> AllowedApplications { get; init; }
    public required IReadOnlyList<string> BlockedApplications { get; init; }
    public required IReadOnlyList<string> AllowedUrls { get; init; }
    public required IReadOnlyList<string> BlockedUrls { get; init; }

    /// <summary>Folders file-transfer commands may write into / read from on this machine (synced so the
    /// Agent re-checks every PushFile/CollectFiles payload locally, not just trusting the server).</summary>
    public IReadOnlyList<TransferFolderDto> TransferFolders { get; init; } = [];
}

/// <summary><c>Direction</c> is "Push" or "Collect".</summary>
public sealed record TransferFolderDto(int Id, string Path, string Direction);
