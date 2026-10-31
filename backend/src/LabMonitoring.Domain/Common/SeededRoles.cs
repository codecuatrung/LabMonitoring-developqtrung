namespace LabMonitoring.Domain.Common;

/// <summary>
/// The three system roles seeded on first migration (SRS FR-8.1). Ids are fixed so
/// role checks and seed data stay stable across environments.
/// </summary>
public static class SeededRoles
{
    public const int AdministratorId = 1;
    public const int TechnicianId = 2;
    public const int TeacherId = 3;

    public const string Administrator = "Administrator";
    public const string Technician = "Technician";
    public const string Teacher = "Teacher";
}
