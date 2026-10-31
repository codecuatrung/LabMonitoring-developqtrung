namespace LabMonitoring.Api.Auth;

/// <summary>Binds the "Keycloak" configuration section (see KEYCLOAK-INTEGRATION.md).</summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Public issuer — must match the token <c>iss</c> exactly (no trailing slash).</summary>
    public required string Authority { get; init; }

    /// <summary>How the API reaches Keycloak for metadata/JWKS. Defaults to <see cref="Authority"/>.</summary>
    public string? InternalAuthority { get; init; }

    /// <summary>Expected <c>aud</c> — the confidential API client id.</summary>
    public required string Audience { get; init; }

    public bool RequireHttpsMetadata { get; init; } = true;

    public string MetadataAddress => $"{(InternalAuthority ?? Authority).TrimEnd('/')}/.well-known/openid-configuration";
}
