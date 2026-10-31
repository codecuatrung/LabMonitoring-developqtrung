using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Teaching;

namespace LabMonitoring.Domain.Sessions;

/// <summary>
/// A workstation allocated within a session, optionally bound to a student, with login/logout
/// timestamps (SRS FR-2.2, FR-6.3). Composite PK (SessionId, ComputerId).
/// </summary>
public class SessionComputer
{
    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;

    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    public DateTime? LoginAt { get; set; }
    public DateTime? LogoutAt { get; set; }
}
