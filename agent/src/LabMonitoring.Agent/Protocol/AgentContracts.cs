namespace LabMonitoring.Agent.Protocol;

// Agent-side copies of the Agent<->Server REST contracts (SRS 8.2). The agent is a standalone
// solution; keep these in sync with backend/src/LabMonitoring.Contracts/Agent/*.

public sealed record AgentRegisterRequest
{
    public required Guid MachineId { get; init; }
    public required string RoomCode { get; init; }
    public required string Hostname { get; init; }
    public string? MacAddress { get; init; }
    public string? AgentVersion { get; init; }
    public string? CpuModel { get; init; }
    public int? CpuCores { get; init; }
    public int? RamTotalMB { get; init; }
    public int? DiskTotalGB { get; init; }
    public string? OsVersion { get; init; }
}

public sealed record AgentRegisterResponse
{
    public int ComputerId { get; init; }
    public string ComputerName { get; init; } = "";
    public string AgentToken { get; init; } = "";
    public string AgentSecret { get; init; } = "";
    public bool Approved { get; init; }
    public int HeartbeatSeconds { get; init; }
}

public sealed record AgentHeartbeatRequest
{
    public string Status { get; init; } = "Online";
    public string? CurrentWindowsUser { get; init; }
    public string? AgentVersion { get; init; }
    public string? LocalIpAddress { get; init; }
    public decimal? CpuUsagePercent { get; init; }
    public decimal? RamUsagePercent { get; init; }
    public decimal? DiskUsagePercent { get; init; }
    public decimal? TemperatureC { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AgentHeartbeatResponse
{
    public bool Acknowledged { get; init; }
    public DateTimeOffset ServerTimeUtc { get; init; }
    public int HeartbeatSeconds { get; init; }
}

public sealed record AgentConfigResponse
{
    public int HeartbeatSeconds { get; init; }
    public int OfflineThresholdSeconds { get; init; }
    public int ScreenshotIntervalSeconds { get; init; }
    public bool Approved { get; init; }
    public IReadOnlyList<string> AllowedApplications { get; init; } = [];
    public IReadOnlyList<string> BlockedApplications { get; init; } = [];
    public IReadOnlyList<string> AllowedUrls { get; init; } = [];
    public IReadOnlyList<string> BlockedUrls { get; init; } = [];

    /// <summary>Folders file-transfer commands may write into / read from on this machine; every PushFile/CollectFiles
    /// payload is re-checked against this list locally (mirrors backend Contracts).</summary>
    public IReadOnlyList<TransferFolderDto> TransferFolders { get; init; } = [];
}

/// <summary><c>Direction</c> is "Push" or "Collect".</summary>
public sealed record TransferFolderDto(int Id, string Path, string Direction);

/// <summary>Final outcome the Agent reports for a command it received (SRS FR-4.5, 8.2).</summary>
public sealed record AgentCommandResultRequest
{
    public required long CommandId { get; init; }
    public required CommandExecutionStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}
