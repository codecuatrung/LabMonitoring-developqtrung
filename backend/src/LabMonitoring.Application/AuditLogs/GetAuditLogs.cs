using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.AuditLogs;

public sealed record AuditLogDto(
    long Id, int? UserId, string? UserName, string Action, string? EntityType, string? EntityId,
    string? Detail, string? IpAddress, DateTime CreatedAt);

/// <summary>Audit trail search (SRS FR-8.3). Administrator only — this is the one place every
/// admin's/agent's actions can be reviewed after the fact.</summary>
public sealed record GetAuditLogsQuery(
    int? UserId, string? Action, DateTime? From, DateTime? To, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<AuditLogDto>>;

internal sealed class GetAuditLogsHandler(IAppDbContext db) : IRequestHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>>
{
    public async Task<PagedResult<AuditLogDto>> Handle(GetAuditLogsQuery request, CancellationToken ct)
    {
        var query = db.AuditLogs.AsQueryable();
        if (request.UserId is { } userId)
            query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(request.Action))
            query = query.Where(a => a.Action.Contains(request.Action));
        if (request.From is { } from)
            query = query.Where(a => a.CreatedAt >= from);
        if (request.To is { } to)
            query = query.Where(a => a.CreatedAt <= to);

        var total = await query.CountAsync(ct);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(
                a.Id, a.UserId, a.User != null ? a.User.FullName : null, a.Action, a.EntityType, a.EntityId,
                a.Detail, a.IpAddress, a.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<AuditLogDto>(items, total, page, pageSize);
    }
}
