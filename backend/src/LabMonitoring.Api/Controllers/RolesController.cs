using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Roles;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>Role CRUD + role→screen assignment (SRS FR-8.1). Any number of custom roles can be
/// created; the 3 seeded roles (Administrator/Technician/Teacher) are protected — see the module
/// doc comment in RoleHandlers.</summary>
[ApiController]
[Route("api/v1/roles")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.Roles)]
public sealed class RolesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct)
        => Ok(await sender.Send(new GetRolesQuery(), ct));

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateRoleInput input, CancellationToken ct)
    {
        var id = await sender.Send(new CreateRoleCommand(input.Name), ct);
        return CreatedAtAction(nameof(List), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateRoleInput input, CancellationToken ct)
    {
        await sender.Send(new UpdateRoleCommand(id, input.Name), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteRoleCommand(id), ct);
        return NoContent();
    }

    [HttpPut("{id:int}/screens")]
    public async Task<IActionResult> SetScreens(int id, [FromBody] IReadOnlyList<int> screenIds, CancellationToken ct)
    {
        await sender.Send(new SetRoleScreensCommand(id, screenIds), ct);
        return NoContent();
    }

    public sealed record CreateRoleInput(string Name);
}
