using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Application.Facilities;
using LabMonitoring.Application.Facilities.Rooms;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/rooms")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.Rooms + "," + Domain.Common.SeededScreens.Catalog)]
public sealed class RoomsController(ISender sender) : ControllerBase
{
    public sealed record RoomInput(int BuildingId, string Name, string Code, int Capacity, string? MapImageUrl);

    /// <summary>Rooms within the caller's scope (SRS FR-8.2).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<RoomDto>>> List(
        [FromQuery] int? buildingId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await sender.Send(new GetRoomsQuery(buildingId, page, pageSize), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoomDetailDto>> Get(int id, CancellationToken ct)
        => Ok(await sender.Send(new GetRoomQuery(id), ct));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Create([FromBody] RoomInput input, CancellationToken ct)
    {
        var id = await sender.Send(new CreateRoomCommand(input.BuildingId, input.Name, input.Code, input.Capacity, input.MapImageUrl), ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Update(int id, [FromBody] RoomInput input, CancellationToken ct)
    {
        await sender.Send(new UpdateRoomCommand(id, input.Name, input.Code, input.Capacity, input.MapImageUrl), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteRoomCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/regenerate-code")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult<string>> RegenerateCode(int id, CancellationToken ct)
        => Ok(new { code = await sender.Send(new RegenerateRoomCodeCommand(id), ct) });

    /// <summary>Purge resource-history rows for every computer in this room. Omit <c>before</c> to delete everything.</summary>
    [HttpDelete("{id:int}/metrics")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult<int>> DeleteMetrics(int id, [FromQuery] DateTime? before, CancellationToken ct)
        => Ok(await sender.Send(new DeleteRoomMetricsCommand(id, before), ct));
}
