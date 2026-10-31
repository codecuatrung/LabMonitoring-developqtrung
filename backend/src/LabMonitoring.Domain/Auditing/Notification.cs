using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Identity;

namespace LabMonitoring.Domain.Auditing;

/// <summary>In-app alert delivered to a user (SRS FR-7.2).</summary>
public class Notification
{
    public long Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;

    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
}
