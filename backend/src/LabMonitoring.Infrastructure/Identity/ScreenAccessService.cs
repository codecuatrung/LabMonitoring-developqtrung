using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LabMonitoring.Infrastructure.Identity;

/// <summary>
/// Resolves + caches per-user screen access (SRS FR-8): union of every role's
/// <c>RoleScreenPermission</c> rows, then <c>UserScreenPermission</c> overrides applied on top
/// (grant adds, revoke removes). Runs on every authorized request (via <c>ScreenPermissionHandler</c>)
/// so results are cached in-memory; the admin console's user/role count is small enough that a
/// single shared cache category, cleared wholesale on any write, is simpler and safer than
/// fine-grained per-key invalidation (e.g. a role-permission edit would otherwise need to find and
/// evict every user who holds that role).
/// </summary>
internal sealed class ScreenAccessService(LabMonitoringDbContext db, IMemoryCache cache) : IScreenAccessService
{
    private const string CacheKeyPrefix = "screen-access:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlySet<string>> GetAccessibleScreenCodesAsync(int userId, bool isAdministrator, CancellationToken ct = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{(isAdministrator ? "admin" : userId.ToString())}";
        if (cache.TryGetValue(cacheKey, out IReadOnlySet<string>? cached) && cached is not null)
            return cached;

        var codes = isAdministrator
            ? await db.FunctionScreens.Where(s => s.IsActive).Select(s => s.Code).ToListAsync(ct)
            : await ResolveForUserAsync(userId, ct);

        var result = codes.ToHashSet();
        cache.Set(cacheKey, (IReadOnlySet<string>)result, CacheTtl);
        return result;
    }

    public void InvalidateCache()
    {
        if (cache is MemoryCache memoryCache)
            memoryCache.Clear();
    }

    private async Task<List<string>> ResolveForUserAsync(int userId, CancellationToken ct)
    {
        var roleGranted = await db.RoleScreenPermissions
            .Where(rp => db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == rp.RoleId))
            .Where(rp => rp.Screen.IsActive)
            .Select(rp => rp.Screen.Code)
            .Distinct()
            .ToListAsync(ct);

        var overrides = await db.UserScreenPermissions
            .Where(up => up.UserId == userId && up.Screen.IsActive)
            .Select(up => new { up.Screen.Code, up.IsGranted })
            .ToListAsync(ct);

        var set = roleGranted.ToHashSet();
        foreach (var o in overrides)
        {
            if (o.IsGranted) set.Add(o.Code);
            else set.Remove(o.Code);
        }

        return set.ToList();
    }
}
