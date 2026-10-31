using LabMonitoring.Domain.Identity;
using LabMonitoring.Domain.Teaching;

namespace LabMonitoring.Domain.Facilities;

public class Room
{
    public int Id { get; set; }

    public int BuildingId { get; set; }
    public Building Building { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Human code, also used by the Agent as the room registration code at self-register.</summary>
    public string Code { get; set; } = null!;

    /// <summary>Max number of workstations (~60/room).</summary>
    public int Capacity { get; set; }

    /// <summary>URL of the room layout image (SRS FR-5.1).</summary>
    public string? MapImageUrl { get; set; }

    public ICollection<Computer> Computers { get; set; } = [];
    public ICollection<Schedule> Schedules { get; set; } = [];
    public ICollection<UserRoomScope> UserScopes { get; set; } = [];
}
