namespace LabMonitoring.Infrastructure.Identity;

/// <summary>Binds the "Auth" configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Keycloak usernames (<c>preferred_username</c>) or emails that are granted the Administrator role
    /// on first sign-in when they have no roles yet. Bootstraps the very first admin (SRS FR-8.1).
    /// </summary>
    public string[] BootstrapAdmins { get; init; } = [];
}
