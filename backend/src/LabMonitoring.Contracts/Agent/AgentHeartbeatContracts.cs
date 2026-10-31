namespace LabMonitoring.Contracts.Agent;

/// <summary>Payload for <c>POST /api/v1/agent/heartbeat</c> (SRS 8.2, FR-1.2/1.4). Sent every 5–10s.</summary>
public sealed record AgentHeartbeatRequest
{
    /// <summary>Agent's self-assessed state: Online / Warning / InUse.</summary>
    public string Status { get; init; } = "Online";

    public string? CurrentWindowsUser { get; init; }
    public string? AgentVersion { get; init; }

    /// <summary>The Agent's own primary LAN IPv4, self-reported (ADR-0014). The connection's
    /// RemoteIpAddress alone isn't reliable — behind Docker Desktop's NAT (dev) or any reverse proxy
    /// (prod) it reflects the last hop, not the real machine, so the server prefers this value when
    /// present and only falls back to the raw connection address for older agents that don't send it.</summary>
    public string? LocalIpAddress { get; init; }

    public decimal? CpuUsagePercent { get; init; }
    public decimal? RamUsagePercent { get; init; }
    public decimal? DiskUsagePercent { get; init; }

    /// <summary>Null when the hardware does not expose CPU temperature (SRS FR-1.3).</summary>
    public decimal? TemperatureC { get; init; }

    /// <summary>When the sample was taken on the client (UTC).</summary>
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AgentHeartbeatResponse
{
    public required bool Acknowledged { get; init; }
    public required DateTimeOffset ServerTimeUtc { get; init; }

    /// <summary>Heartbeat interval the server wants the Agent to use (may change over time).</summary>
    public required int HeartbeatSeconds { get; init; }
}
