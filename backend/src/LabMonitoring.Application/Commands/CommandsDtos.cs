namespace LabMonitoring.Application.Commands;

public sealed record CommandResultDto(
    long Id, long CommandId, int ComputerId, string ComputerName, string Status,
    DateTime? ExecutedAt, string? ErrorMessage);

public sealed record CommandDto(
    long Id, int? ComputerId, string? ComputerName, int? RoomId, string? RoomName,
    int? SessionId, string CommandType, string? Payload,
    int IssuedById, string IssuedByName, DateTime IssuedAt,
    IReadOnlyList<CommandResultDto> Results);

/// <summary>Outcome of issuing a command: how many machines it was actually sent to.</summary>
public sealed record IssueCommandResultDto(long CommandId, int TargetCount);
