namespace LabMonitoring.Domain.Facilities;

/// <summary>
/// Time-series resource sample, one per heartbeat (SRS FR-1.2). High insert rate — indexed on
/// (ComputerId, RecordedAt); retention/rollup handled by a background job (M5).
/// </summary>
public class ComputerMetric
{
    public long Id { get; set; }

    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public DateTime RecordedAt { get; set; }

    public decimal? CpuUsagePercent { get; set; }
    public decimal? RamUsagePercent { get; set; }
    public decimal? DiskUsagePercent { get; set; }

    /// <summary>Null when the hardware does not expose CPU temperature (SRS FR-1.3).</summary>
    public decimal? TemperatureC { get; set; }
}
