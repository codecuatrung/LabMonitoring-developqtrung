using System.Security.Claims;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Domain.Common;

namespace LabMonitoring.Api.Auth;

/// <summary>Reads the current admin user from the JWT + the claims added by <see cref="UserProvisioningMiddleware"/>.</summary>
internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? KeycloakUserId =>
        Guid.TryParse(Principal?.FindFirst("sub")?.Value ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : null;

    public int? UserId =>
        int.TryParse(Principal?.FindFirst(LabClaimTypes.LocalUserId)?.Value, out var id) ? id : null;

    public string? Username => Principal?.FindFirst("preferred_username")?.Value ?? Principal?.Identity?.Name;
    public string? Email => Principal?.FindFirst("email")?.Value;
    public string? FullName => Principal?.FindFirst("name")?.Value;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(LabClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool IsAdministrator => IsInRole(SeededRoles.Administrator);

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
