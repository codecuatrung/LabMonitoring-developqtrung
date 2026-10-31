namespace LabMonitoring.Domain.Facilities;

/// <summary>Hardware inventory for a workstation (1–1 with <see cref="Computer"/>).</summary>
public class ComputerSpec
{
    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public string? CpuModel { get; set; }
    public int? CpuCores { get; set; }
    public int? RamTotalMB { get; set; }
    public int? DiskTotalGB { get; set; }
    public string? OsVersion { get; set; }
    public DateTime UpdatedAt { get; set; }
}
