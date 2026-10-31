using LabMonitoring.Domain.Common;

namespace LabMonitoring.Domain.Facilities;

/// <summary>Current state of a workstation (1–1 with <see cref="Computer"/>).</summary>
public class ComputerStatus
{
    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public ComputerState CurrentStatus { get; set; } = ComputerState.Offline;

    /// <summary>Used to flag Offline when older than the configured threshold (default 30s).</summary>
    public DateTime? LastHeartbeatAt { get; set; }

    public string? CurrentWindowsUser { get; set; }
    public string? LastIpAddress { get; set; }

    /// <summary>Set while a CPU/RAM threshold breach is active (SRS FR-7.1); cleared once the metric
    /// recovers, so the next breach re-alerts instead of staying silenced forever.</summary>
    public DateTime? CpuAlertRaisedAt { get; set; }
    public DateTime? RamAlertRaisedAt { get; set; }
}
