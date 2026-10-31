using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Infrastructure.Identity;

/// <summary>
/// Room visibility for the current user (SRS FR-8.2). Administrator → unrestricted (null);
/// otherwise the union of the user's <c>UserRoomScopes</c>.
/// </summary>
internal sealed class RoomScope(ICurrentUser currentUser, LabMonitoringDbContext db) : IRoomScope
{
    private IReadOnlySet<int>? _cache;
    private bool _resolved;

    public async Task<IReadOnlySet<int>?> GetAllowedRoomIdsAsync(CancellationToken ct = default)
    {
        if (_resolved)
            return _cache;

        if (currentUser.IsAdministrator)
        {
            _resolved = true;
            return _cache = null;
        }

        var userId = currentUser.UserId
            ?? throw new InvalidOperationException("Current user has not been provisioned.");

        var ids = await db.UserRoomScopes
            .Where(s => s.UserId == userId)
            .Select(s => s.RoomId)
            .ToListAsync(ct);

        _resolved = true;
        return _cache = ids.ToHashSet();
    }

    public async Task<bool> CanAccessRoomAsync(int roomId, CancellationToken ct = default)
    {
        var allowed = await GetAllowedRoomIdsAsync(ct);
        return allowed is null || allowed.Contains(roomId);
    }
}
