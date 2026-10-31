using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Application.Facilities;
using LabMonitoring.Application.Facilities.Computers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/computers")]
[Authorize(Policy = AuthPolicies.AdminConsole)]
public sealed class ComputersController(ISender sender) : ControllerBase
{
    public sealed record ComputerInput(string Name, int? PositionX, int? PositionY, bool IsActive);

    /// <summary>List computers, filtered by room / status / pending-approval, within the caller's scope.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoomComputerDto>>> List(
        [FromQuery] int? roomId, [FromQuery] string? status, [FromQuery] bool? pendingApproval, CancellationToken ct)
        => Ok(await sender.Send(new GetComputersQuery(roomId, status, pendingApproval), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ComputerDetailDto>> Get(int id, CancellationToken ct)
        => Ok(await sender.Send(new GetComputerQuery(id), ct));

    /// <summary>Resource history for the charts (SRS section 8.3).</summary>
    [HttpGet("{id:int}/metrics")]
    public async Task<ActionResult<PagedResult<MetricPointDto>>> Metrics(
        int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        => Ok(await sender.Send(new GetComputerMetricsQuery(id, from, to,
            page == 0 ? 1 : page, pageSize == 0 ? 30 : pageSize), ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, [FromBody] ComputerInput input, CancellationToken ct)
    {
        await sender.Send(new UpdateComputerCommand(id, input.Name, input.PositionX, input.PositionY, input.IsActive), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult> Approve(int id, CancellationToken ct)
    {
        await sender.Send(new ApproveComputerCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult> Reject(int id, CancellationToken ct)
    {
        await sender.Send(new RejectComputerCommand(id), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteComputerCommand(id), ct);
        return NoContent();
    }

    /// <summary>Purge resource-history rows for this machine. Omit <c>before</c> to delete everything.</summary>
    [HttpDelete("{id:int}/metrics")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult<int>> DeleteMetrics(int id, [FromQuery] DateTime? before, CancellationToken ct)
        => Ok(await sender.Send(new DeleteComputerMetricsCommand(id, before), ct));
}
