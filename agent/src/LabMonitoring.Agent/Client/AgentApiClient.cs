using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LabMonitoring.Agent.Identity;
using LabMonitoring.Agent.Protocol;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Client;

public sealed class AgentRegistrationConflictException(string message) : Exception(message);

/// <summary>Typed client for the Agent REST endpoints (SRS 8.2). Adds the AgentToken header per call.</summary>
public sealed class AgentApiClient(HttpClient http, CredentialStore credentials, ILogger<AgentApiClient> logger)
{
    public async Task<AgentRegisterResponse> RegisterAsync(AgentRegisterRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/v1/agent/register", request, ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new AgentRegistrationConflictException(await response.Content.ReadAsStringAsync(ct));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AgentRegisterResponse>(ct)
            ?? throw new InvalidOperationException("Empty registration response.");

        logger.LogInformation("Registered as computer {ComputerId} ({Name}), approved={Approved}",
            result.ComputerId, result.ComputerName, result.Approved);
        return result;
    }

    public async Task<AgentHeartbeatResponse?> SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken ct)
    {
        using var msg = Authorized(HttpMethod.Post, "api/v1/agent/heartbeat");
        msg.Content = JsonContent.Create(request);

        using var response = await http.SendAsync(msg, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Heartbeat rejected: {Status}", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<AgentHeartbeatResponse>(ct);
    }

    /// <summary>Reports the final outcome of a command (SRS FR-4.5). Best-effort: logged, not thrown, so
    /// a delivery failure doesn't crash the heartbeat loop that calls this.</summary>
    public async Task SendCommandResultAsync(AgentCommandResultRequest request, CancellationToken ct)
    {
        try
        {
            using var msg = Authorized(HttpMethod.Post, "api/v1/agent/command-result");
            msg.Content = JsonContent.Create(request);

            using var response = await http.SendAsync(msg, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Command result report rejected for command {CommandId}: {Status}", request.CommandId, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to report result for command {CommandId}", request.CommandId);
        }
    }

    /// <summary>Streams the current LabAgent update package (SRS H1 "auto-update"). Caller disposes the
    /// returned response (and therefore its content stream) once done reading; null means the server
    /// has nothing uploaded yet or rejected the request.</summary>
    public async Task<HttpResponseMessage?> DownloadUpdatePackageAsync(CancellationToken ct)
    {
        var msg = Authorized(HttpMethod.Get, "api/v1/agent/update-package");
        var response = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode)
            return response;

        logger.LogWarning("Update package download failed: {Status}", (int)response.StatusCode);
        response.Dispose();
        return null;
    }

    public async Task<AgentConfigResponse?> GetConfigAsync(CancellationToken ct)
    {
        using var msg = Authorized(HttpMethod.Get, "api/v1/agent/config");
        using var response = await http.SendAsync(msg, ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Config fetch failed: {Status}", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<AgentConfigResponse>(ct);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var creds = credentials.Load() ?? throw new InvalidOperationException("Agent is not registered.");
        var msg = new HttpRequestMessage(method, path);
        msg.Headers.Authorization = new AuthenticationHeaderValue("AgentToken", creds.AgentToken);
        return msg;
    }
}
