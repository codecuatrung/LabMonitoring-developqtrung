using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Teaching;
using LabMonitoring.Application.Teaching.Schedules;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/schedules")]
[Authorize(Policy = AuthPolicies.AdminConsole)]
public sealed class SchedulesController(ISender sender) : ControllerBase
{
    public sealed record ScheduleInput(
        int ClassId, int RoomId, byte DayOfWeek, TimeOnly StartTime, TimeOnly EndTime,
        DateOnly EffectiveFrom, DateOnly EffectiveTo);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScheduleDto>>> List(
        [FromQuery] int? classId, [FromQuery] int? roomId, CancellationToken ct)
        => Ok(await sender.Send(new GetSchedulesQuery(classId, roomId), ct));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Create([FromBody] ScheduleInput input, CancellationToken ct)
    {
        var id = await sender.Send(new CreateScheduleCommand(
            input.ClassId, input.RoomId, input.DayOfWeek, input.StartTime, input.EndTime,
            input.EffectiveFrom, input.EffectiveTo), ct);
        return CreatedAtAction(nameof(List), new { id }, new { id });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteScheduleCommand(id), ct);
        return NoContent();
    }
}
