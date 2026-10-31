using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;

namespace LabMonitoring.Domain.Policies;

/// <summary>Allow/Block rule for an application, per room or system-wide (SRS FR-4.4).</summary>
public class ApplicationPolicy
{
    public int Id { get; set; }

    /// <summary>Null = applies to the whole system.</summary>
    public int? RoomId { get; set; }
    public Room? Room { get; set; }

    public int ApplicationId { get; set; }
    public ManagedApplication Application { get; set; } = null!;

    public PolicyType PolicyType { get; set; }
}
