using System.Security.Claims;
using LabMonitoring.Application.Common.Abstractions;

namespace LabMonitoring.Api.Auth;

/// <summary>
/// After JWT authentication, maps the Keycloak <c>sub</c> to a local <c>Users</c> row (creating it on
/// first sign-in) and enriches the principal with <c>lab:uid</c> + <c>lab:role</c> claims so
/// authorization policies and <see cref="ICurrentUser"/> can use the local model.
/// Runs once per request, only for JWT-authenticated calls.
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IUserProvisioner provisioner)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true
            && user.Identity.AuthenticationType != AuthSchemes.AgentToken
            && user.FindFirst(LabClaimTypes.LocalUserId) is null)
        {
            var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(sub, out var subject))
            {
                var identity = new KeycloakIdentity(
                    subject,
                    user.FindFirst("preferred_username")?.Value ?? subject.ToString(),
                    user.FindFirst("email")?.Value,
                    user.FindFirst("name")?.Value);

                var provisioned = await provisioner.EnsureProvisionedAsync(identity, context.RequestAborted);

                var claims = new List<Claim> { new(LabClaimTypes.LocalUserId, provisioned.UserId.ToString()) };
                claims.AddRange(provisioned.Roles.Select(r => new Claim(LabClaimTypes.Role, r)));
                user.AddIdentity(new ClaimsIdentity(claims));
            }
        }

        await next(context);
    }
}
