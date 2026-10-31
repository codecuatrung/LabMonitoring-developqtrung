using LabMonitoring.Domain.Identity;

namespace LabMonitoring.Domain.Teaching;

public class Class
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Subject { get; set; } = null!;

    public int TeacherId { get; set; }
    public User Teacher { get; set; } = null!;

    public string? Semester { get; set; }

    public ICollection<ClassStudent> ClassStudents { get; set; } = [];
    public ICollection<Schedule> Schedules { get; set; } = [];
}
