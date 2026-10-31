namespace LabMonitoring.Domain.Teaching;

/// <summary>
/// A student, recognised in a session via the Windows login name reported by the Agent
/// (SRS FR-2.1). Not an admin account.
/// </summary>
public class Student
{
    public int Id { get; set; }
    public string StudentCode { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }

    public ICollection<ClassStudent> ClassStudents { get; set; } = [];
}
