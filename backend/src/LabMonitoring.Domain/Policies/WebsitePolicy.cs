using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;

namespace LabMonitoring.Domain.Policies;

/// <summary>Allow/Block rule for a website domain, per room or system-wide (coordinated with DNS/firewall).</summary>
public class WebsitePolicy
{
    public int Id { get; set; }

    /// <summary>Null = applies to the whole system.</summary>
    public int? RoomId { get; set; }
    public Room? Room { get; set; }

    public string Domain { get; set; } = null!;

    public PolicyType PolicyType { get; set; }
}
