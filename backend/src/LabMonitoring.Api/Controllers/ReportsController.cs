using System.Globalization;
using System.Text;
using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Reports;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>Usage reports (SRS FR-7.4).</summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.ReportsUsage)]
public sealed class ReportsController(ISender sender) : ControllerBase
{
    [HttpGet("usage")]
    public async Task<ActionResult<UsageReportDto>> Usage(
        [FromQuery] int? roomId, [FromQuery] int? classId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await sender.Send(new GetUsageReportQuery(roomId, classId, from, to), ct));

    [HttpGet("usage/export")]
    public async Task<IActionResult> ExportUsage(
        [FromQuery] int? roomId, [FromQuery] int? classId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var report = await sender.Send(new GetUsageReportQuery(roomId, classId, from, to), ct);

        var csv = new StringBuilder();
        csv.AppendLine("Phong,So ca,Gio may,So sinh vien");
        foreach (var row in report.Rows)
        {
            csv.AppendLine(string.Join(',',
                CsvField(row.RoomName), row.SessionCount,
                row.MachineHours.ToString("0.##", CultureInfo.InvariantCulture), row.StudentCount));
        }
        csv.AppendLine();
        csv.AppendLine(string.Join(',', "Tong",
            report.TotalSessions, report.TotalMachineHours.ToString("0.##", CultureInfo.InvariantCulture), report.TotalStudents));

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
        return File(bytes, "text/csv", $"bao-cao-su-dung-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string CsvField(string value)
        => value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
