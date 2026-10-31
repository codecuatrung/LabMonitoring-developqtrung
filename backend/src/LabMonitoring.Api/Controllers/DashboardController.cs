using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Dashboard;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.Dashboard)]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    /// <summary>Overview counts scoped to the caller (UC-02).</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken ct)
        => Ok(await sender.Send(new GetDashboardSummaryQuery(), ct));
}
