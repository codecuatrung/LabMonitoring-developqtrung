using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Screens;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>Function screen registry (SRS FR-8): every screen a role/user permission can target.
/// Registering a new screen is what makes it assignable — the Angular route itself is still code.</summary>
[ApiController]
[Route("api/v1/screens")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.Screens)]
public sealed class ScreensController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScreenDto>>> List(CancellationToken ct)
        => Ok(await sender.Send(new GetScreensQuery(), ct));

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] ScreenInput input, CancellationToken ct)
    {
        var id = await sender.Send(new CreateScreenCommand(input.Code, input.Name, input.Route, input.Icon, input.DisplayOrder, input.ParentId), ct);
        return CreatedAtAction(nameof(List), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateScreenInput input, CancellationToken ct)
    {
        await sender.Send(new UpdateScreenCommand(id, input.Name, input.Route, input.Icon, input.DisplayOrder, input.IsActive, input.ParentId), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteScreenCommand(id), ct);
        return NoContent();
    }

    public sealed record ScreenInput(string Code, string Name, string? Route, string? Icon, int DisplayOrder, int? ParentId);
    public sealed record UpdateScreenInput(string Name, string? Route, string? Icon, int DisplayOrder, bool IsActive, int? ParentId);
}
