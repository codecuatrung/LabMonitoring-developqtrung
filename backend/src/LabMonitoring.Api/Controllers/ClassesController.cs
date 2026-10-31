using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Application.Teaching;
using LabMonitoring.Application.Teaching.Classes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/classes")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.Classes)]
public sealed class ClassesController(ISender sender) : ControllerBase
{
    public sealed record ClassInput(string Code, string Name, string Subject, int TeacherId, string? Semester);

    [HttpGet]
    public async Task<ActionResult<PagedResult<ClassDto>>> List(
        [FromQuery] int? teacherId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await sender.Send(new GetClassesQuery(teacherId, page, pageSize), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClassDetailDto>> Get(int id, CancellationToken ct)
        => Ok(await sender.Send(new GetClassQuery(id), ct));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Create([FromBody] ClassInput input, CancellationToken ct)
    {
        var id = await sender.Send(new CreateClassCommand(input.Code, input.Name, input.Subject, input.TeacherId, input.Semester), ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Update(int id, [FromBody] ClassInput input, CancellationToken ct)
    {
        await sender.Send(new UpdateClassCommand(id, input.Code, input.Name, input.Subject, input.TeacherId, input.Semester), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteClassCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/students/{studentId:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Enroll(int id, int studentId, CancellationToken ct)
    {
        await sender.Send(new EnrollStudentCommand(id, studentId), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/students/{studentId:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Unenroll(int id, int studentId, CancellationToken ct)
    {
        await sender.Send(new UnenrollStudentCommand(id, studentId), ct);
        return NoContent();
    }
}
