using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Commands;
using LabMonitoring.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>Control commands to one machine or a whole room (SRS FR-4).</summary>
[ApiController]
[Route("api/v1/commands")]
[Authorize(Policy = AuthPolicies.AdminConsole)]
public sealed class CommandsController(ISender sender) : ControllerBase
{
    public sealed record IssueCommandInput(
        int? ComputerId, int? RoomId, int? SessionId, CommandKind CommandType,
        string? Text, string? ExecutablePath, string? Url, bool Confirm = false);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CommandDto>>> List(
        [FromQuery] int? roomId, [FromQuery] int? computerId, [FromQuery] int? sessionId, CancellationToken ct)
        => Ok(await sender.Send(new GetCommandsQuery(roomId, computerId, sessionId), ct));

    [HttpGet("{id:long}/results")]
    public async Task<ActionResult<IReadOnlyList<CommandResultDto>>> Results(long id, CancellationToken ct)
        => Ok(await sender.Send(new GetCommandResultsQuery(id), ct));

    [HttpPost]
    public async Task<ActionResult<IssueCommandResultDto>> Issue([FromBody] IssueCommandInput input, CancellationToken ct)
    {
        var result = await sender.Send(new IssueCommandCommand(
            input.ComputerId, input.RoomId, input.SessionId, input.CommandType,
            input.Text, input.ExecutablePath, input.Url, input.Confirm), ct);

        return CreatedAtAction(nameof(Results), new { id = result.CommandId }, result);
    }

    public sealed record WakeInput(int? ComputerId, int? RoomId);

    /// <summary>Wake-on-LAN for one machine or every machine in a room (not a signed Agent command
    /// — see <see cref="WakeComputersCommand"/>).</summary>
    [HttpPost("wake")]
    public async Task<ActionResult<WakeResultDto>> Wake([FromBody] WakeInput input, CancellationToken ct)
        => Ok(await sender.Send(new WakeComputersCommand(input.ComputerId, input.RoomId), ct));
}
