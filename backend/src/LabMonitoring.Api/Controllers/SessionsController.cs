using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Application.Sessions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/sessions")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.Sessions)]
public sealed class SessionsController(ISender sender) : ControllerBase
{
    public sealed record StartInput(int RoomId, int ClassId, int? ScheduleId);
    public sealed record AllocateInput(bool Auto, IReadOnlyList<StudentAssignment>? Assignments);

    [HttpGet]
    public async Task<ActionResult<PagedResult<SessionDto>>> List(
        [FromQuery] int? roomId, [FromQuery] int? classId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await sender.Send(new GetSessionsQuery(roomId, classId, status, page, pageSize), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SessionDetailDto>> Get(int id, CancellationToken ct)
        => Ok(await sender.Send(new GetSessionQuery(id), ct));

    /// <summary>Session summary report (SRS FR-6.4).</summary>
    [HttpGet("{id:int}/report")]
    public async Task<ActionResult<SessionReportDto>> Report(int id, CancellationToken ct)
        => Ok(await sender.Send(new GetSessionReportQuery(id), ct));

    [HttpPost("start")]
    [Authorize(Policy = AuthPolicies.TeacherOrAdmin)]
    public async Task<ActionResult> Start([FromBody] StartInput input, CancellationToken ct)
    {
        var id = await sender.Send(new StartSessionCommand(input.RoomId, input.ClassId, input.ScheduleId), ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPost("{id:int}/end")]
    [Authorize(Policy = AuthPolicies.TeacherOrAdmin)]
    public async Task<ActionResult> End(int id, CancellationToken ct)
    {
        await sender.Send(new EndSessionCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/allocate")]
    [Authorize(Policy = AuthPolicies.TeacherOrAdmin)]
    public async Task<ActionResult> Allocate(int id, [FromBody] AllocateInput input, CancellationToken ct)
    {
        await sender.Send(new AllocateStudentsCommand(id, input.Auto, input.Assignments), ct);
        return NoContent();
    }
}
