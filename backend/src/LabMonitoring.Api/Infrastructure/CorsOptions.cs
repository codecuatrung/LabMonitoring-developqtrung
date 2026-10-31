namespace LabMonitoring.Api.Infrastructure;

/// <summary>Binds the "Cors" configuration section (allowed browser origins for the Angular SPA).</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "LabMonitoringSpa";

    public string[] AllowedOrigins { get; init; } = [];
}
