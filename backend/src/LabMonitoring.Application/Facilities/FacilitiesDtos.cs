namespace LabMonitoring.Application.Facilities;

public sealed record BuildingDto(int Id, string Name, string Code, int RoomCount);

public sealed record RoomDto(
    int Id, int BuildingId, string BuildingName, string Name, string Code,
    int Capacity, string? MapImageUrl, int ComputerCount,
    int OnlineCount = 0, int InUseCount = 0, int WarningCount = 0, int PendingCount = 0);

public sealed record RoomComputerDto(
    int Id, string Name, string Status, bool IsApproved, bool IsActive,
    int? PositionX, int? PositionY, string? CurrentWindowsUser,
    decimal? CpuUsagePercent, decimal? RamUsagePercent, DateTime? LastHeartbeatAt);

public sealed record RoomDetailDto(
    int Id, string Name, string Code, int Capacity, string? MapImageUrl,
    int BuildingId, string BuildingName, IReadOnlyList<RoomComputerDto> Computers);

public sealed record ComputerDetailDto(
    int Id, int RoomId, string RoomName, string Name, Guid MachineId,
    string Status, bool IsApproved, bool IsActive,
    string? MacAddress, int? PositionX, int? PositionY, string? AgentVersion,
    DateTime CreatedAt,
    string? CurrentWindowsUser, string? LastIpAddress, DateTime? LastHeartbeatAt,
    ComputerSpecDto? Spec);

public sealed record ComputerSpecDto(
    string? CpuModel, int? CpuCores, int? RamTotalMB, int? DiskTotalGB, string? OsVersion, DateTime UpdatedAt);

public sealed record MetricPointDto(
    DateTime RecordedAt, decimal? CpuUsagePercent, decimal? RamUsagePercent,
    decimal? DiskUsagePercent, decimal? TemperatureC);
