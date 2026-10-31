namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>Claims read from a validated Keycloak access token.</summary>
public sealed record KeycloakIdentity(
    Guid Subject,
    string Username,
    string? Email,
    string? FullName);

/// <summary>The local account behind a Keycloak identity, after provisioning.</summary>
public sealed record ProvisionedUser(int UserId, IReadOnlyList<string> Roles);

/// <summary>
/// Just-in-time provisioning: maps a Keycloak identity to a local <c>Users</c> row (keyed on
/// <c>sub</c>), creating it on first sign-in. Never joins on email.
/// </summary>
public interface IUserProvisioner
{
    Task<ProvisionedUser> EnsureProvisionedAsync(KeycloakIdentity identity, CancellationToken ct = default);
}
