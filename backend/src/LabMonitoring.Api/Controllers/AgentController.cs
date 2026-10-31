using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Agent;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Contracts.Agent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>Agent-facing endpoints (SRS 8.2). All calls require HTTPS.</summary>
[ApiController]
[Route("api/v1/agent")]
public sealed class AgentController(IAgentAccessService agentAccess) : ControllerBase
{
    /// <summary>Self-registration on first connect. Unauthenticated; gated by the room code.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AgentRegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] AgentRegisterRequest request, CancellationToken ct)
    {
        try
        {
            var result = await agentAccess.RegisterAsync(request, ct);
            return Ok(result);
        }
        catch (AgentRegistrationException ex)
        {
            return Problem(
                title: ex.IsConflict ? "Conflict" : "Bad Request",
                detail: ex.Message,
                statusCode: ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Periodic status + resource sample (SRS FR-1.2). Sent every 5–10s.</summary>
    [HttpPost("heartbeat")]
    [Authorize(AuthenticationSchemes = AuthSchemes.AgentToken)]
    [ProducesResponseType(typeof(AgentHeartbeatResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentHeartbeatResponse>> Heartbeat([FromBody] AgentHeartbeatRequest request, CancellationToken ct)
    {
        var agent = CurrentAgent();
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        return Ok(await agentAccess.RecordHeartbeatAsync(agent, request, clientIp, ct));
    }

    /// <summary>Final outcome of a dispatched command (SRS FR-4.5, 8.2). AckCommand over SignalR
    /// already covers the earlier "received it" step.</summary>
    [HttpPost("command-result")]
    [Authorize(AuthenticationSchemes = AuthSchemes.AgentToken)]
    public async Task<IActionResult> CommandResult([FromBody] AgentCommandResultRequest request, CancellationToken ct)
    {
        await agentAccess.RecordCommandResultAsync(CurrentAgent(), request, ct);
        return NoContent();
    }

    /// <summary>Current polling configuration and app/URL whitelist for this machine's room.</summary>
    [HttpGet("config")]
    [Authorize(AuthenticationSchemes = AuthSchemes.AgentToken)]
    [ProducesResponseType(typeof(AgentConfigResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentConfigResponse>> GetConfig(CancellationToken ct)
    {
        var agent = CurrentAgent();
        return Ok(await agentAccess.GetConfigAsync(agent, ct));
    }

    /// <summary>The current LabAgent build (SRS H1 "auto-update") — always the most recently uploaded
    /// package, matching the version/sha256 an <c>UpdateAgent</c> CommandEnvelope carries.</summary>
    [HttpGet("update-package")]
    [Authorize(AuthenticationSchemes = AuthSchemes.AgentToken)]
    public async Task<IActionResult> UpdatePackage(CancellationToken ct)
    {
        CurrentAgent(); // require a valid AgentToken; no further scoping needed, every agent gets the same package
        var stream = await agentAccess.OpenLatestUpdatePackageAsync(ct);
        return stream is null
            ? Problem(title: "Not Found", detail: "No Agent package has been uploaded yet.", statusCode: StatusCodes.Status404NotFound)
            : File(stream, "application/zip");
    }

    /// <summary>File of a PushFile transfer (file transfer). Only the machines the transfer targets may fetch it;
    /// Range requests are supported so an interrupted download resumes. Over the concurrency cap → 429 + Retry-After.</summary>
    [HttpGet("transfers/{id:int}/file")]
    [Authorize(AuthenticationSchemes = AuthSchemes.AgentToken)]
    public async Task<IActionResult> TransferFile(int id, [FromServices] ITransferThrottle throttle, CancellationToken ct)
    {
        var lease = throttle.TryAcquire();
        if (lease is null)
        {
            Response.Headers.RetryAfter = "10";
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var (content, _) = await agentAccess.OpenTransferFileAsync(CurrentAgent(), id, ct);
            HttpContext.Response.RegisterForDispose(lease); // released when the response finishes streaming
            return File(content, "application/octet-stream", enableRangeProcessing: true);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>The zip an Agent built for a CollectFiles transfer (raw request body).</summary>
    [HttpPost("transfers/{id:int}/collected")]
    [Authorize(AuthenticationSchemes = AuthSchemes.AgentToken)]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadCollected(int id, [FromQuery] int fileCount, [FromQuery] bool truncated, CancellationToken ct)
    {
        await agentAccess.SaveCollectedAsync(CurrentAgent(), id, Request.Body, fileCount, truncated, ct);
        return NoContent();
    }

    private AgentPrincipal CurrentAgent()
    {
        int ComputerId() => int.Parse(User.FindFirst(LabClaimTypes.ComputerId)!.Value);
        int RoomId() => int.Parse(User.FindFirst(LabClaimTypes.RoomId)!.Value);
        var machineId = Guid.Parse(User.FindFirst(LabClaimTypes.MachineId)!.Value);
        var approved = User.FindFirst(LabClaimTypes.AgentApproved)?.Value == "true";
        return new AgentPrincipal(ComputerId(), machineId, RoomId(), string.Empty, approved);
    }
}
