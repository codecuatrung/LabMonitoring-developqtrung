using LabMonitoring.Application.Common.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace LabMonitoring.Api.Auth;

/// <summary>Satisfied when the current user can access ANY of <see cref="ScreenCodes"/> (SRS FR-8)
/// — most controllers map to exactly one screen, but a few (e.g. Rooms, shared between the "Phòng
/// máy" and "Danh mục" screens) are legitimately reachable from more than one. Resolved from role +
/// per-user override via <see cref="IScreenAccessService"/>; Administrators always pass.</summary>
public sealed class ScreenPermissionRequirement(IReadOnlyList<string> screenCodes) : IAuthorizationRequirement
{
    public IReadOnlyList<string> ScreenCodes { get; } = screenCodes;
}

internal sealed class ScreenPermissionHandler(IScreenAccessService screenAccess)
    : AuthorizationHandler<ScreenPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ScreenPermissionRequirement requirement)
    {
        if (!int.TryParse(context.User.FindFirst(LabClaimTypes.LocalUserId)?.Value, out var userId))
            return; // not a JWT-authenticated Web Admin user (e.g. Agent) — no screen access

        var isAdministrator = context.User.FindAll(LabClaimTypes.Role)
            .Any(c => string.Equals(c.Value, "Administrator", StringComparison.OrdinalIgnoreCase));

        var accessible = await screenAccess.GetAccessibleScreenCodesAsync(userId, isAdministrator);
        if (requirement.ScreenCodes.Any(accessible.Contains))
            context.Succeed(requirement);
    }
}

/// <summary>
/// Lets controllers declare <c>[Authorize(Policy = "screen:rooms")]</c> (or
/// <c>"screen:rooms,catalog"</c> for a controller reachable from more than one screen) for any
/// registered <c>FunctionScreen.Code</c> without a matching <c>AddPolicy</c> call at startup —
/// adding a new screen is then purely a data change (insert a <c>FunctionScreens</c> row + seed
/// <c>RoleScreenPermissions</c>), not a redeploy of the policy table. Falls back to the default
/// provider for every other policy name (the existing static <c>role:*</c>/<c>admin-console</c>/…
/// policies are unaffected).
/// </summary>
internal sealed class ScreenAuthorizationPolicyProvider(
    Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "screen:";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            var screenCodes = policyName[PolicyPrefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var policy = new AuthorizationPolicyBuilder(AuthSchemes.Jwt)
                .RequireAuthenticatedUser()
                .AddRequirements(new ScreenPermissionRequirement(screenCodes))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
