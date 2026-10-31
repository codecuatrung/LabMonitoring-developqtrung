using LabMonitoring.Domain.Facilities;

namespace LabMonitoring.Domain.Teaching;

/// <summary>Recurring class timetable slot (SRS FR-6.1).</summary>
public class Schedule
{
    public int Id { get; set; }

    public int ClassId { get; set; }
    public Class Class { get; set; } = null!;

    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;

    /// <summary>0–6.</summary>
    public byte DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
}
