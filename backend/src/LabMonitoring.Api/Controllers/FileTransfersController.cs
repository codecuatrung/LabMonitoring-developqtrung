using LabMonitoring.Api.Auth;
using LabMonitoring.Application.FileTransfers;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Transfers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>Copy files to client machines and collect folders back (file transfer). Files move over REST
/// between the Agents and the API; the start of every job is a signed command (see IssueCommandHandler).</summary>
[ApiController]
[Route("api/v1/file-transfers")]
[Authorize(Policy = "screen:" + SeededScreens.FileTransfer)]
public sealed class FileTransfersController(ISender sender) : ControllerBase
{
    // Upper bound for the HTTP layer only; the real, configurable limit (FileTransfer:MaxPushBytes) is enforced while saving.
    private const long HardUploadLimit = 2L * 1024 * 1024 * 1024;

    public sealed class PushFileForm
    {
        public IFormFile? File { get; set; }
        public int? ComputerId { get; set; }
        public int? RoomId { get; set; }
        public int FolderId { get; set; }
        public bool Overwrite { get; set; }
        public bool Confirm { get; set; }
    }

    public sealed record CollectRequest(int? ComputerId, int? RoomId, int FolderId, string? Filter, bool Confirm);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FileTransferSummary>>> List(
        [FromQuery] int? roomId, [FromQuery] TransferDirection? direction, CancellationToken ct)
        => Ok(await sender.Send(new GetFileTransfersQuery(roomId, direction), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FileTransferDetail>> Get(int id, CancellationToken ct)
        => Ok(await sender.Send(new GetFileTransferQuery(id), ct));

    [HttpPost("push")]
    [RequestSizeLimit(HardUploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = HardUploadLimit)]
    public async Task<ActionResult<PushTransferResult>> Push([FromForm] PushFileForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0)
            return Problem(title: "Bad Request", detail: "A non-empty file is required.", statusCode: StatusCodes.Status400BadRequest);

        await using var content = form.File.OpenReadStream();
        var result = await sender.Send(new CreatePushTransferCommand(
            form.ComputerId, form.RoomId, form.FolderId, form.Overwrite, form.Confirm, Path.GetFileName(form.File.FileName), content), ct);
        return Ok(result);
    }

    [HttpPost("collect")]
    public async Task<ActionResult<PushTransferResult>> Collect([FromBody] CollectRequest request, CancellationToken ct)
        => Ok(await sender.Send(new CreateCollectTransferCommand(
            request.ComputerId, request.RoomId, request.FolderId, request.Filter, request.Confirm), ct));

    [HttpGet("{id:int}/collected/{computerId:int}")]
    public async Task<IActionResult> DownloadCollected(int id, int computerId, CancellationToken ct)
    {
        var download = await sender.Send(new OpenCollectedFileQuery(id, computerId), ct);
        return File(download.Content, "application/zip", download.DownloadName);
    }
}

/// <summary>The allowed-folder list (which client folders may be written to / collected from).
/// Anyone with the screen may read it to fill the pickers; only Administrators change it.</summary>
[ApiController]
[Route("api/v1/transfer-folders")]
[Authorize(Policy = "screen:" + SeededScreens.FileTransfer)]
public sealed class TransferFoldersController(ISender sender) : ControllerBase
{
    public sealed record SaveFolderRequest(int? RoomId, string Path, string Direction, string Label, bool IsActive = true);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TransferFolderView>>> List(
        [FromQuery] int? roomId, [FromQuery] TransferDirection? direction, [FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(await sender.Send(new GetTransferFoldersQuery(roomId, direction, includeInactive), ct));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult<int>> Create([FromBody] SaveFolderRequest request, CancellationToken ct)
        => Ok(await sender.Send(new SaveTransferFolderCommand(null, request.RoomId, request.Path, request.Direction, request.Label, request.IsActive), ct));

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<ActionResult<int>> Update(int id, [FromBody] SaveFolderRequest request, CancellationToken ct)
        => Ok(await sender.Send(new SaveTransferFolderCommand(id, request.RoomId, request.Path, request.Direction, request.Label, request.IsActive), ct));

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.Administrator)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await sender.Send(new DeleteTransferFolderCommand(id), ct);
        return NoContent();
    }
}
