using LabMonitoring.Domain.Facilities;

namespace LabMonitoring.Domain.Identity;

/// <summary>
/// Limits a Technician / Teacher to the rooms they may see and control (SRS FR-8.2). Composite PK.
/// </summary>
public class UserRoomScope
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
}
