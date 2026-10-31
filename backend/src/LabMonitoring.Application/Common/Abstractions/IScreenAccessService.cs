namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// Resolves which function screens (SRS FR-8) a user can access: every screen their roles grant,
/// with per-user overrides (<c>UserScreenPermission</c>) applied on top. Administrators bypass this
/// entirely (see implementation) — callers should not special-case that themselves.
/// </summary>
public interface IScreenAccessService
{
    /// <summary>Screen <c>Code</c>s the user can access. <paramref name="isAdministrator"/> bypasses
    /// role/override resolution entirely and returns every active screen (matches
    /// <c>ICurrentUser.IsAdministrator</c> — pass that straight through). Cached per user — call
    /// <see cref="InvalidateCache"/> after any write that could change the result.</summary>
    Task<IReadOnlySet<string>> GetAccessibleScreenCodesAsync(int userId, bool isAdministrator, CancellationToken ct = default);

    /// <summary>Clears the whole permission cache. Call after editing roles, screens,
    /// role-screen permissions, user-screen overrides, or a user's own roles.</summary>
    void InvalidateCache();
}
