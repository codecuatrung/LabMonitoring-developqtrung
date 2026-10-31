using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Dashboard;

public sealed record DashboardSummaryDto(
    int Rooms, int Computers, int Online, int Offline, int Warning, int InUse, int PendingApproval);

/// <summary>Top-level counts for the dashboard, limited to the caller's room scope (UC-02).</summary>
public sealed record GetDashboardSummaryQuery : IRequest<DashboardSummaryDto>;

internal sealed class GetDashboardSummaryHandler(IAppDbContext db, IRoomScope scope)
    : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken ct)
    {
        var allowed = await scope.GetAllowedRoomIdsAsync(ct);

        var rooms = db.Rooms.AsQueryable();
        var computers = db.Computers.Where(c => c.IsActive);
        if (allowed is not null)
        {
            rooms = rooms.Where(r => allowed.Contains(r.Id));
            computers = computers.Where(c => allowed.Contains(c.RoomId));
        }

        var byState = await computers
            .GroupBy(c => c.Status != null ? c.Status.CurrentStatus : ComputerState.Offline)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(ComputerState s) => byState.FirstOrDefault(x => x.State == s)?.Count ?? 0;

        return new DashboardSummaryDto(
            Rooms: await rooms.CountAsync(ct),
            Computers: byState.Sum(x => x.Count),
            Online: Count(ComputerState.Online),
            Offline: Count(ComputerState.Offline),
            Warning: Count(ComputerState.Warning),
            InUse: Count(ComputerState.InUse),
            PendingApproval: await computers.CountAsync(c => !c.IsApproved, ct));
    }
}
