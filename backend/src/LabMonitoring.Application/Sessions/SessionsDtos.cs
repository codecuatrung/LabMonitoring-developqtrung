namespace LabMonitoring.Application.Sessions;

public sealed record SessionDto(
    int Id, int RoomId, string RoomName, int ClassId, string ClassName,
    int TeacherId, string TeacherName, DateTime StartedAt, DateTime? EndedAt, string Status,
    int ComputerCount, int StudentCount);

public sealed record SessionComputerDto(
    int ComputerId, string ComputerName, int? StudentId, string? StudentCode, string? StudentName,
    DateTime? LoginAt, DateTime? LogoutAt);

public sealed record SessionDetailDto(
    int Id, int RoomId, string RoomName, int ClassId, string ClassName,
    int TeacherId, string TeacherName, DateTime StartedAt, DateTime? EndedAt, string Status,
    IReadOnlyList<SessionComputerDto> Computers);

public sealed record SessionReportDto(
    int Id, string ClassName, string RoomName, string TeacherName,
    DateTime StartedAt, DateTime? EndedAt, double DurationMinutes,
    int ComputersUsed, int StudentsLoggedIn, int StudentsEnrolled,
    IReadOnlyList<SessionComputerDto> Computers);
