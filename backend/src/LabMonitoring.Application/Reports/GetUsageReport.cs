using LabMonitoring.Application.Common.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Reports;

public sealed record UsageReportRoomRow(int RoomId, string RoomName, int SessionCount, double MachineHours, int StudentCount);

public sealed record UsageReportDto(
    DateTime From, DateTime To, int TotalSessions, double TotalMachineHours, int TotalStudents,
    IReadOnlyList<UsageReportRoomRow> Rows);

/// <summary>Usage report (SRS FR-7.4): sessions/machine-hours/students by room, filterable by room,
/// class and time window. Respects room scope (FR-8.2) like every other report/list endpoint.</summary>
public sealed record GetUsageReportQuery(int? RoomId, int? ClassId, DateTime? From, DateTime? To) : IRequest<UsageReportDto>;

internal sealed class GetUsageReportHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetUsageReportQuery, UsageReportDto>
{
    public async Task<UsageReportDto> Handle(GetUsageReportQuery request, CancellationToken ct)
    {
        var from = request.From ?? DateTime.UtcNow.AddDays(-30);
        var to = request.To ?? DateTime.UtcNow;

        var query = db.Sessions.Where(s => s.StartedAt >= from && s.StartedAt <= to);
        if (request.RoomId is { } roomId)
            query = query.Where(s => s.RoomId == roomId);
        if (request.ClassId is { } classId)
            query = query.Where(s => s.ClassId == classId);

        var allowed = await scope.GetAllowedRoomIdsAsync(ct);
        if (allowed is not null)
            query = query.Where(s => allowed.Contains(s.RoomId));

        var sessions = await query
            .Select(s => new
            {
                s.RoomId,
                RoomName = s.Room.Name,
                Computers = s.Computers.Select(c => new { c.LoginAt, c.LogoutAt, c.StudentId }).ToList(),
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var rows = sessions
            .GroupBy(s => (s.RoomId, s.RoomName))
            .Select(g => new UsageReportRoomRow(
                g.Key.RoomId,
                g.Key.RoomName,
                g.Count(),
                g.SelectMany(s => s.Computers)
                    .Where(c => c.LoginAt is not null)
                    .Sum(c => ((c.LogoutAt ?? now) - c.LoginAt!.Value).TotalHours),
                g.SelectMany(s => s.Computers)
                    .Where(c => c.StudentId is not null)
                    .Select(c => c.StudentId!.Value)
                    .Distinct()
                    .Count()))
            .OrderBy(r => r.RoomName)
            .ToList();

        var totalStudents = sessions
            .SelectMany(s => s.Computers)
            .Where(c => c.StudentId is not null)
            .Select(c => c.StudentId!.Value)
            .Distinct()
            .Count();

        return new UsageReportDto(from, to, sessions.Count, rows.Sum(r => r.MachineHours), totalStudents, rows);
    }
}
