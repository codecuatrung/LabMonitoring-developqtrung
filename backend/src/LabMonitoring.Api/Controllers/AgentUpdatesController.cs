using LabMonitoring.Application.Agent;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>Uploaded LabAgent build history — the admin-facing half of auto-update (SRS H1). The
/// Agent's own download of the current package is a separate, AgentToken-authenticated endpoint on
/// <see cref="AgentController"/>, not here.</summary>
[ApiController]
[Route("api/v1/agent-updates")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.AgentUpdates)]
public sealed class AgentUpdatesController(ISender sender) : ControllerBase
{
    private const long MaxUploadBytes = 500 * 1024 * 1024; // 500 MB — a self-contained win-x64 publish is a few hundred MB at most

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentPackageDto>>> List(CancellationToken ct)
        => Ok(await sender.Send(new GetAgentPackagesQuery(), ct));

    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<int>> Upload([FromForm] string version, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem(title: "Bad Request", detail: "A non-empty file is required.", statusCode: StatusCodes.Status400BadRequest);

        await using var content = file.OpenReadStream();
        var id = await sender.Send(new UploadAgentPackageCommand(version, content), ct);
        return Ok(new { id });
    }
}
