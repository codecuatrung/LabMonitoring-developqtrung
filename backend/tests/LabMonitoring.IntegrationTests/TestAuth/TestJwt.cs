using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace LabMonitoring.IntegrationTests.TestAuth;

/// <summary>
/// Mints access tokens the test host will accept — the JWT bearer handler is post-configured with
/// <see cref="SecurityKey"/> and issuer <see cref="Issuer"/> in <see cref="AdminApiFactory"/>.
/// </summary>
public static class TestJwt
{
    public const string Issuer = "https://test.labmonitoring";
    public const string Audience = "labmonitoring-api";

    private const string KeyMaterial = "lab-monitoring-integration-tests-signing-key-0123456789";
    public static readonly SymmetricSecurityKey SecurityKey = new(Encoding.UTF8.GetBytes(KeyMaterial));

    public static string ForUser(string preferredUsername, string? email = null, Guid? sub = null, string? name = null)
    {
        var claims = new List<Claim>
        {
            new("sub", (sub ?? Guid.NewGuid()).ToString()),
            new("preferred_username", preferredUsername),
            new("name", name ?? preferredUsername),
        };
        if (email is not null)
            claims.Add(new Claim("email", email));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
