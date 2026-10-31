using LabMonitoring.Application.Common.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace LabMonitoring.Api.Auth;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddLabMonitoringAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var keycloak = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
            ?? throw new InvalidOperationException("Missing 'Keycloak' configuration section.");

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddAuthentication(AuthSchemes.Jwt)
            .AddJwtBearer(AuthSchemes.Jwt, options =>
            {
                options.MetadataAddress = keycloak.MetadataAddress;
                options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
                options.Audience = keycloak.Audience;
                options.MapInboundClaims = false; // keep raw claim names: sub, preferred_username, …
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = keycloak.Authority,
                    ValidateAudience = true,
                    ValidAudience = keycloak.Audience,
                    ValidateLifetime = true,
                    NameClaimType = "preferred_username",
                    RoleClaimType = LabClaimTypes.Role,
                };
            })
            .AddScheme<AuthenticationSchemeOptions, AgentTokenAuthenticationHandler>(AuthSchemes.AgentToken, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Administrator, p => p.RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthSchemes.Jwt).RequireClaim(LabClaimTypes.Role, "Administrator"))
            .AddPolicy(AuthPolicies.Technician, p => p.RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthSchemes.Jwt).RequireClaim(LabClaimTypes.Role, "Technician"))
            .AddPolicy(AuthPolicies.Teacher, p => p.RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthSchemes.Jwt).RequireClaim(LabClaimTypes.Role, "Teacher"))
            .AddPolicy(AuthPolicies.AdminConsole, p => p.RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthSchemes.Jwt)
                .RequireClaim(LabClaimTypes.Role, "Administrator", "Technician", "Teacher"))
            .AddPolicy(AuthPolicies.TeacherOrAdmin, p => p.RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthSchemes.Jwt)
                .RequireClaim(LabClaimTypes.Role, "Administrator", "Teacher"))
            .AddPolicy(AuthPolicies.ApprovedAgent, p => p.RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthSchemes.AgentToken)
                .RequireClaim(LabClaimTypes.AgentApproved, "true"));

        // Dynamic "screen:{code}" policies (SRS FR-8) — replaces the default provider; every other
        // policy name (role:*, admin-console, …) still falls through to it unchanged.
        services.AddSingleton<IAuthorizationPolicyProvider, ScreenAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, ScreenPermissionHandler>();

        return services;
    }
}
