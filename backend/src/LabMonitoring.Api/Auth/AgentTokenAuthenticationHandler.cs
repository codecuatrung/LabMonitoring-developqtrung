using System.Security.Claims;
using System.Text.Encodings.Web;
using LabMonitoring.Application.Agent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Api.Auth;

/// <summary>
/// Authenticates Agent requests carrying <c>Authorization: AgentToken &lt;token&gt;</c> (SRS 8.1).
/// The token is per-machine; the resulting principal carries the computer / room / approval claims.
/// </summary>
public sealed class AgentTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IAgentAccessService agentAccess)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return AuthenticateResult.NoResult();

        var raw = header.ToString();
        const string prefix = AuthSchemes.AgentToken + " ";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = raw[prefix.Length..].Trim();
        var agent = await agentAccess.AuthenticateAsync(token, Context.RequestAborted);
        if (agent is null)
            return AuthenticateResult.Fail("Invalid AgentToken.");

        var identity = new ClaimsIdentity(
        [
            new Claim(LabClaimTypes.ComputerId, agent.ComputerId.ToString()),
            new Claim(LabClaimTypes.MachineId, agent.MachineId.ToString()),
            new Claim(LabClaimTypes.RoomId, agent.RoomId.ToString()),
            new Claim(LabClaimTypes.AgentApproved, agent.IsApproved ? "true" : "false"),
            new Claim(ClaimTypes.Name, $"agent:{agent.MachineId}"),
        ], AuthSchemes.AgentToken);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSchemes.AgentToken);
        return AuthenticateResult.Success(ticket);
    }
}
