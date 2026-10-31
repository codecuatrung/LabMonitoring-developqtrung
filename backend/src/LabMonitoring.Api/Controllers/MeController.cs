using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Screens;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>The current Web Admin user's own resolved data — no role restriction beyond being
/// authenticated, since even a custom role with no built-in policy match still needs to know its own
/// accessible screens to render the menu (SRS FR-8).</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize(AuthenticationSchemes = AuthSchemes.Jwt)]
public sealed class MeController(ISender sender) : ControllerBase
{
    [HttpGet("screens")]
    public async Task<ActionResult<IReadOnlyList<ScreenDto>>> Screens(CancellationToken ct)
        => Ok(await sender.Send(new GetMyScreensQuery(), ct));
}
