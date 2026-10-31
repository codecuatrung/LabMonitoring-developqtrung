using LabMonitoring.Api.Auth;
using LabMonitoring.Application.AuditLogs;
using LabMonitoring.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>Audit trail search (SRS FR-8.3).</summary>
[ApiController]
[Route("api/v1/audit-logs")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.AuditLogs)]
public sealed class AuditLogsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List(
        [FromQuery] int? userId, [FromQuery] string? action,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        => Ok(await sender.Send(new GetAuditLogsQuery(userId, action, from, to,
            page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), ct));
}
