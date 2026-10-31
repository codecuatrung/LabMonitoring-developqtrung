using LabMonitoring.Domain.Identity;

namespace LabMonitoring.Domain.Auditing;

/// <summary>
/// Immutable record of every admin action and control command (SRS FR-8.3): who, when, IP, outcome.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>Null when the system generated the entry.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>e.g. "Command.Shutdown", "User.Create".</summary>
    public string Action { get; set; } = null!;

    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>JSON detail of the before/after change.</summary>
    public string? Detail { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }
}
