using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Teaching;
using LabMonitoring.Application.Teaching.Students;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/students")]
[Authorize(Policy = AuthPolicies.AdminConsole)]
public sealed class StudentsController(ISender sender) : ControllerBase
{
    public sealed record StudentInput(string StudentCode, string FullName, string? Email);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StudentDto>>> List([FromQuery] string? search, CancellationToken ct)
        => Ok(await sender.Send(new GetStudentsQuery(search), ct));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Create([FromBody] StudentInput input, CancellationToken ct)
    {
        var id = await sender.Send(new CreateStudentCommand(input.StudentCode, input.FullName, input.Email), ct);
        return CreatedAtAction(nameof(List), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Update(int id, [FromBody] StudentInput input, CancellationToken ct)
    {
        await sender.Send(new UpdateStudentCommand(id, input.StudentCode, input.FullName, input.Email), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteStudentCommand(id), ct);
        return NoContent();
    }
}
