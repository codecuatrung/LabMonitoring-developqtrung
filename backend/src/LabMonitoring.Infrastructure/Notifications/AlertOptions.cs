namespace LabMonitoring.Infrastructure.Notifications;

/// <summary>Binds "Alerts" — resource thresholds for the CPU/RAM alert engine (SRS FR-7.1).</summary>
public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    public decimal CpuThresholdPercent { get; init; } = 90;
    public decimal RamThresholdPercent { get; init; } = 90;
}
