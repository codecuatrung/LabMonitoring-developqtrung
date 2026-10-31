using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace LabMonitoring.IntegrationTests.TestAuth;

/// <summary>
/// Test host whose JWT bearer handler accepts <see cref="TestJwt"/> tokens (no Keycloak needed) and
/// whose config bootstraps "test-admin" as an Administrator. Use <see cref="CreateAdminClient"/>.
/// </summary>
public sealed class AdminApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUsername = "test-admin";
    public static readonly Guid AdminSub = Guid.Parse("11111111-1111-1111-1111-111111111111");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdmins:0"] = AdminUsername,
        }));

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.RequireHttpsMetadata = false;
                options.Configuration = new OpenIdConnectConfiguration { Issuer = TestJwt.Issuer };
                options.Configuration.SigningKeys.Add(TestJwt.SecurityKey);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestJwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = TestJwt.Audience,
                    ValidateLifetime = true,
                    IssuerSigningKey = TestJwt.SecurityKey,
                    NameClaimType = "preferred_username",
                    RoleClaimType = "lab:role",
                };
            });
        });
    }

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.ForUser(AdminUsername, "test-admin@lab.local", AdminSub));
        return client;
    }
}
