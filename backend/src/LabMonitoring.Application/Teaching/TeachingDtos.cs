namespace LabMonitoring.Application.Teaching;

public sealed record ClassDto(
    int Id, string Code, string Name, string Subject,
    int TeacherId, string TeacherName, string? Semester, int StudentCount);

public sealed record ClassDetailDto(
    int Id, string Code, string Name, string Subject,
    int TeacherId, string TeacherName, string? Semester,
    IReadOnlyList<StudentDto> Students);

public sealed record StudentDto(int Id, string StudentCode, string FullName, string? Email);

public sealed record ScheduleDto(
    int Id, int ClassId, string ClassName, int RoomId, string RoomName,
    byte DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, DateOnly EffectiveFrom, DateOnly EffectiveTo);
