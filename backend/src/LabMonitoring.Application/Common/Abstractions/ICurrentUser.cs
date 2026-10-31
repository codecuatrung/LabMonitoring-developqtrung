namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>The admin user behind the current request (resolved from the Keycloak JWT + local provisioning).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Keycloak <c>sub</c>.</summary>
    Guid? KeycloakUserId { get; }

    /// <summary>Local <c>Users.Id</c>. Null until provisioning has run for this request.</summary>
    int? UserId { get; }

    string? Username { get; }
    string? Email { get; }
    string? FullName { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsAdministrator { get; }
    bool IsInRole(string role);
}
