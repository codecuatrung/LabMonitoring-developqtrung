using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Identity;
using LabMonitoring.Domain.Teaching;

namespace LabMonitoring.Domain.Sessions;

/// <summary>A lab session: a class taught in a room over a time window (SRS FR-6.2).</summary>
public class Session
{
    public int Id { get; set; }

    /// <summary>Null when the session is started ad-hoc, outside the timetable.</summary>
    public int? ScheduleId { get; set; }
    public Schedule? Schedule { get; set; }

    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public int ClassId { get; set; }
    public Class Class { get; set; } = null!;

    public int TeacherId { get; set; }
    public User Teacher { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;

    public ICollection<SessionComputer> Computers { get; set; } = [];
}
