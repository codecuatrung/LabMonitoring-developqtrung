using LabMonitoring.Domain.Identity;

namespace LabMonitoring.Domain.Facilities;

/// <summary>
/// One uploaded LabAgent build (SRS H1 "auto-update"). The most recently uploaded row is the
/// "current" deployable version — <see cref="IssueCommandHandler"/> reads it when building an
/// <c>UpdateAgent</c> command payload, and <c>GET /api/v1/agent/update-package</c> always serves it.
/// </summary>
public class AgentPackage
{
    public int Id { get; set; }

    /// <summary>Matches <c>AgentRegistrationService.AgentVersion</c> on the build that was zipped.</summary>
    public string Version { get; set; } = null!;

    public string FileName { get; set; } = null!;
    public long FileSizeBytes { get; set; }

    /// <summary>Lowercase hex SHA-256 of the zip — the Agent verifies this after downloading before
    /// applying the update (the hash itself is tamper-evident because it travels inside the
    /// HMAC-signed CommandEnvelope payload, not because the file is separately signed).</summary>
    public string Sha256 { get; set; } = null!;

    public int? UploadedById { get; set; }
    public User? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
}
