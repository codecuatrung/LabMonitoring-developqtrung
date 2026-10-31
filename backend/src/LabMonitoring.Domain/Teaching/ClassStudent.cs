namespace LabMonitoring.Domain.Teaching;

/// <summary>Many-to-many Class ↔ Student (composite PK).</summary>
public class ClassStudent
{
    public int ClassId { get; set; }
    public Class Class { get; set; } = null!;

    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
}
