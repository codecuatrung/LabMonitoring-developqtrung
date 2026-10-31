using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Users;

public sealed record UserSummaryDto(int Id, string Username, string FullName, string? Email, bool IsActive, IReadOnlyList<string> Roles);

/// <summary>
/// Read model reused by two callers: admin pickers (e.g. choosing a class's teacher — active only,
/// the default) and the M6 user management list (<c>IncludeInactive=true</c>, so a deactivated
/// account can still be found and reactivated).
/// </summary>
public sealed record GetUsersQuery(string? Role = null, bool IncludeInactive = false, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<UserSummaryDto>>;

internal sealed class GetUsersHandler(IAppDbContext db) : IRequestHandler<GetUsersQuery, PagedResult<UserSummaryDto>>
{
    public async Task<PagedResult<UserSummaryDto>> Handle(GetUsersQuery request, CancellationToken ct)
    {
        var query = db.Users.AsQueryable();
        if (!request.IncludeInactive)
            query = query.Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(request.Role))
            query = query.Where(u => u.UserRoles.Any(ur => ur.Role.Name == request.Role));

        query = query.OrderBy(u => u.FullName);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserSummaryDto(u.Id, u.Username, u.FullName, u.Email, u.IsActive,
                u.UserRoles.Select(ur => ur.Role.Name).ToList()))
            .ToListAsync(ct);

        return new PagedResult<UserSummaryDto>(items, total, page, pageSize);
    }
}
