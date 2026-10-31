using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Application.Facilities;
using LabMonitoring.Application.Facilities.Buildings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/buildings")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.Catalog)]
public sealed class BuildingsController(ISender sender) : ControllerBase
{
    public sealed record BuildingInput(string Name, string Code);

    [HttpGet]
    public async Task<ActionResult<PagedResult<BuildingDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await sender.Send(new GetBuildingsQuery(page, pageSize), ct));

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] BuildingInput input, CancellationToken ct)
    {
        var id = await sender.Send(new CreateBuildingCommand(input.Name, input.Code), ct);
        return CreatedAtAction(nameof(List), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, [FromBody] BuildingInput input, CancellationToken ct)
    {
        await sender.Send(new UpdateBuildingCommand(id, input.Name, input.Code), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteBuildingCommand(id), ct);
        return NoContent();
    }
}
