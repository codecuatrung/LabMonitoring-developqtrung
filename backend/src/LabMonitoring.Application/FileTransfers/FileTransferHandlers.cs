using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using LabMonitoring.Application.Commands;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Transfers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.FileTransfers;

// ---- DTOs ------------------------------------------------------------------------------------

public sealed record TransferFolderView(int Id, int? RoomId, string? RoomName, string Path, string Direction, string Label, bool IsActive);

public sealed record FileTransferSummary(
    int Id, string Direction, int? RoomId, string? RoomName, int? ComputerId, string? ComputerName,
    string FolderPath, string? FileName, long SizeBytes, string? Filter, bool Overwrite,
    long? CommandId, string? CreatedByName, DateTime CreatedAt);

public sealed record TransferTargetResult(
    int ComputerId, string ComputerName, string Status, DateTime? ExecutedAt, string? ErrorMessage,
    long? CollectedBytes, int? CollectedFileCount, bool? CollectedTruncated);

public sealed record FileTransferDetail(FileTransferSummary Transfer, IReadOnlyList<TransferTargetResult> Targets);

public sealed record CollectedDownload(Stream Content, string DownloadName);

// ---- Shared helpers --------------------------------------------------------------------------

internal static class TransferGuards
{
    /// <summary>Resolves the room a target belongs to and checks the caller's room scope.</summary>
    public static async Task<int> ResolveRoomAsync(IAppDbContext db, IRoomScope scope, int? computerId, int? roomId, CancellationToken ct)
    {
        int room;
        if (computerId is { } cid)
        {
            room = await db.Computers.Where(c => c.Id == cid).Select(c => (int?)c.RoomId).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException(nameof(Computer), cid);
        }
        else
        {
            room = roomId ?? throw new ArgumentException("Specify exactly one of computerId or roomId.");
            if (!await db.Rooms.AnyAsync(r => r.Id == room, ct))
                throw new NotFoundException(nameof(Room), room);
        }

        if (!await scope.CanAccessRoomAsync(room, ct))
            throw new ForbiddenException();
        return room;
    }

    public static async Task<TransferFolder> ResolveFolderAsync(
        IAppDbContext db, int folderId, TransferDirection direction, int roomId, CancellationToken ct)
        => await db.TransferFolders.FirstOrDefaultAsync(
               f => f.Id == folderId && f.IsActive && f.Direction == direction && (f.RoomId == null || f.RoomId == roomId), ct)
           ?? throw new ForbiddenException("This folder is not in the allowed list for the selected room.");
}

// ---- Allowed folders (admin CRUD) --------------------------------------------------------------

public sealed record GetTransferFoldersQuery(int? RoomId, TransferDirection? Direction, bool IncludeInactive)
    : IRequest<IReadOnlyList<TransferFolderView>>;

internal sealed class GetTransferFoldersHandler(IAppDbContext db, IRoomScope scope)
    : IRequestHandler<GetTransferFoldersQuery, IReadOnlyList<TransferFolderView>>
{
    public async Task<IReadOnlyList<TransferFolderView>> Handle(GetTransferFoldersQuery request, CancellationToken ct)
    {
        var query = db.TransferFolders.AsQueryable();
        if (!request.IncludeInactive)
            query = query.Where(f => f.IsActive);
        if (request.Direction is { } direction)
            query = query.Where(f => f.Direction == direction);

        if (request.RoomId is { } roomId)
        {
            if (!await scope.CanAccessRoomAsync(roomId, ct))
                throw new ForbiddenException();
            query = query.Where(f => f.RoomId == null || f.RoomId == roomId);
        }
        else if (await scope.GetAllowedRoomIdsAsync(ct) is { } allowed)
        {
            query = query.Where(f => f.RoomId == null || allowed.Contains(f.RoomId.Value));
        }

        var rows = await query
            .OrderBy(f => f.Direction).ThenBy(f => f.Label)
            .Select(f => new { f.Id, f.RoomId, RoomName = f.Room != null ? f.Room.Name : null, f.Path, f.Direction, f.Label, f.IsActive })
            .ToListAsync(ct);

        return rows.Select(f => new TransferFolderView(f.Id, f.RoomId, f.RoomName, f.Path, f.Direction.ToString(), f.Label, f.IsActive)).ToList();
    }
}

public sealed record SaveTransferFolderCommand(int? Id, int? RoomId, string Path, string Direction, string Label, bool IsActive) : IRequest<int>;

public sealed partial class SaveTransferFolderValidator : AbstractValidator<SaveTransferFolderCommand>
{
    // Absolute drive path ("D:\BaiTap") or one that starts with an environment variable ("%PUBLIC%\Desktop").
    [GeneratedRegex(@"^([A-Za-z]:\\|%[A-Za-z_][A-Za-z0-9_]*%(\\|$))")]
    private static partial Regex AllowedPathStart();

    public SaveTransferFolderValidator()
    {
        RuleFor(x => x.Path).NotEmpty().MaximumLength(500)
            .Must(p => AllowedPathStart().IsMatch(p ?? string.Empty))
            .WithMessage("Path must be an absolute drive path (D:\\Folder) or start with a %VARIABLE% such as %PUBLIC%.")
            .Must(p => !(p ?? string.Empty).Split('\\', '/').Contains(".."))
            .WithMessage("Path may not contain '..'.");
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Direction).Must(d => Enum.TryParse<TransferDirection>(d, ignoreCase: true, out _))
            .WithMessage("Direction must be 'Push' or 'Collect'.");
    }
}

internal sealed class SaveTransferFolderHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<SaveTransferFolderCommand, int>
{
    public async Task<int> Handle(SaveTransferFolderCommand request, CancellationToken ct)
    {
        if (request.RoomId is { } roomId && !await db.Rooms.AnyAsync(r => r.Id == roomId, ct))
            throw new NotFoundException(nameof(Room), roomId);

        var direction = Enum.Parse<TransferDirection>(request.Direction, ignoreCase: true);
        var path = request.Path.Trim().TrimEnd('\\', '/');

        TransferFolder folder;
        if (request.Id is { } id)
        {
            folder = await db.TransferFolders.FirstOrDefaultAsync(f => f.Id == id, ct)
                ?? throw new NotFoundException(nameof(TransferFolder), id);
        }
        else
        {
            folder = new TransferFolder { CreatedAt = DateTime.UtcNow };
            db.TransferFolders.Add(folder);
        }

        folder.RoomId = request.RoomId;
        folder.Path = path;
        folder.Direction = direction;
        folder.Label = request.Label.Trim();
        folder.IsActive = request.IsActive;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = request.Id is null ? "TransferFolder.Create" : "TransferFolder.Update",
            EntityType = nameof(TransferFolder),
            EntityId = request.Id?.ToString(),
            Detail = JsonSerializer.Serialize(new { path, direction = direction.ToString(), request.RoomId, request.IsActive }),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return folder.Id;
    }
}

public sealed record DeleteTransferFolderCommand(int Id) : IRequest;

internal sealed class DeleteTransferFolderHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<DeleteTransferFolderCommand>
{
    public async Task Handle(DeleteTransferFolderCommand request, CancellationToken ct)
    {
        var folder = await db.TransferFolders.FirstOrDefaultAsync(f => f.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(TransferFolder), request.Id);

        db.TransferFolders.Remove(folder);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "TransferFolder.Delete",
            EntityType = nameof(TransferFolder),
            EntityId = request.Id.ToString(),
            Detail = JsonSerializer.Serialize(new { folder.Path }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}

// ---- Push (server → clients) -----------------------------------------------------------------

/// <summary>Stores the uploaded file and issues a signed <c>PushFile</c> command to one machine or a whole room.</summary>
public sealed record CreatePushTransferCommand(
    int? ComputerId, int? RoomId, int FolderId, bool Overwrite, bool Confirm, string FileName, Stream Content)
    : IRequest<PushTransferResult>;

public sealed record PushTransferResult(int TransferId, long CommandId, int TargetCount);

internal sealed class CreatePushTransferHandler(
    IAppDbContext db, IRoomScope scope, ICurrentUser currentUser, IFileTransferStore store,
    IFileTransferPolicy policy, ISender sender) : IRequestHandler<CreatePushTransferCommand, PushTransferResult>
{
    public async Task<PushTransferResult> Handle(CreatePushTransferCommand request, CancellationToken ct)
    {
        if (request.ComputerId.HasValue == request.RoomId.HasValue)
            throw new ArgumentException("Specify exactly one of computerId or roomId.");
        if (request.RoomId.HasValue && !request.Confirm)
            throw new ArgumentException("Pushing a file to every machine in a room requires confirm=true.");

        var nameError = FileNameRules.Validate(request.FileName, policy.BlockedExtensions);
        if (nameError is not null)
            throw new ArgumentException(nameError);

        var roomId = await TransferGuards.ResolveRoomAsync(db, scope, request.ComputerId, request.RoomId, ct);
        var folder = await TransferGuards.ResolveFolderAsync(db, request.FolderId, TransferDirection.Push, roomId, ct);
        var userId = currentUser.UserId ?? throw new ForbiddenException();

        var transfer = new FileTransfer
        {
            Direction = TransferDirection.Push,
            RoomId = request.RoomId,
            ComputerId = request.ComputerId,
            TransferFolderId = folder.Id,
            FolderPath = folder.Path,
            FileName = request.FileName,
            Overwrite = request.Overwrite,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
        };
        db.FileTransfers.Add(transfer);
        await db.SaveChangesAsync(ct); // assigns transfer.Id, which names the stored file

        try
        {
            var (sha256, size) = await store.SavePushFileAsync(transfer.Id, request.Content, policy.MaxPushBytes, ct);
            if (size == 0)
                throw new ArgumentException("The file is empty.");
            transfer.Sha256 = sha256;
            transfer.SizeBytes = size;
            await db.SaveChangesAsync(ct);

            var issued = await sender.Send(new IssueCommandCommand(
                request.ComputerId, request.RoomId, null, CommandKind.PushFile, null, null, null, request.Confirm,
                TransferId: transfer.Id), ct);

            transfer.CommandId = issued.CommandId;
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "FileTransfer.Push",
                EntityType = nameof(FileTransfer),
                EntityId = transfer.Id.ToString(),
                Detail = JsonSerializer.Serialize(new
                {
                    request.FileName, size, sha256, folder = folder.Path, request.ComputerId, request.RoomId,
                    request.Overwrite, issued.CommandId, issued.TargetCount,
                }),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);

            return new PushTransferResult(transfer.Id, issued.CommandId, issued.TargetCount);
        }
        catch
        {
            store.DeleteTransfer(transfer.Id);
            db.FileTransfers.Remove(transfer);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}

// ---- Collect (clients → server) --------------------------------------------------------------

public sealed record CreateCollectTransferCommand(int? ComputerId, int? RoomId, int FolderId, string? Filter, bool Confirm)
    : IRequest<PushTransferResult>;

public sealed class CreateCollectTransferValidator : AbstractValidator<CreateCollectTransferCommand>
{
    public CreateCollectTransferValidator()
    {
        RuleFor(x => x.Filter).MaximumLength(200)
            .Must(f => string.IsNullOrWhiteSpace(f) || f.IndexOfAny(['\\', '/', ':', '<', '>', '"', '|']) < 0)
            .WithMessage("Filter may only contain file-name patterns such as *.docx;*.pdf (no folders).");
    }
}

internal sealed class CreateCollectTransferHandler(
    IAppDbContext db, IRoomScope scope, ICurrentUser currentUser, IFileTransferPolicy policy, ISender sender)
    : IRequestHandler<CreateCollectTransferCommand, PushTransferResult>
{
    public async Task<PushTransferResult> Handle(CreateCollectTransferCommand request, CancellationToken ct)
    {
        if (request.ComputerId.HasValue == request.RoomId.HasValue)
            throw new ArgumentException("Specify exactly one of computerId or roomId.");
        if (request.RoomId.HasValue && !request.Confirm)
            throw new ArgumentException("Collecting files from every machine in a room requires confirm=true.");

        var roomId = await TransferGuards.ResolveRoomAsync(db, scope, request.ComputerId, request.RoomId, ct);
        var folder = await TransferGuards.ResolveFolderAsync(db, request.FolderId, TransferDirection.Collect, roomId, ct);
        var userId = currentUser.UserId ?? throw new ForbiddenException();

        var transfer = new FileTransfer
        {
            Direction = TransferDirection.Collect,
            RoomId = request.RoomId,
            ComputerId = request.ComputerId,
            TransferFolderId = folder.Id,
            FolderPath = folder.Path,
            Filter = string.IsNullOrWhiteSpace(request.Filter) ? null : request.Filter.Trim(),
            MaxBytes = policy.MaxCollectBytes,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
        };
        db.FileTransfers.Add(transfer);
        await db.SaveChangesAsync(ct);

        try
        {
            var issued = await sender.Send(new IssueCommandCommand(
                request.ComputerId, request.RoomId, null, CommandKind.CollectFiles, null, null, null, request.Confirm,
                TransferId: transfer.Id), ct);

            transfer.CommandId = issued.CommandId;
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "FileTransfer.Collect",
                EntityType = nameof(FileTransfer),
                EntityId = transfer.Id.ToString(),
                Detail = JsonSerializer.Serialize(new { folder = folder.Path, transfer.Filter, request.ComputerId, request.RoomId, issued.CommandId, issued.TargetCount }),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);

            return new PushTransferResult(transfer.Id, issued.CommandId, issued.TargetCount);
        }
        catch
        {
            db.FileTransfers.Remove(transfer);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}

// ---- History / detail / download -----------------------------------------------------------------

public sealed record GetFileTransfersQuery(int? RoomId, TransferDirection? Direction) : IRequest<IReadOnlyList<FileTransferSummary>>;

internal sealed class GetFileTransfersHandler(IAppDbContext db, IRoomScope scope)
    : IRequestHandler<GetFileTransfersQuery, IReadOnlyList<FileTransferSummary>>
{
    public async Task<IReadOnlyList<FileTransferSummary>> Handle(GetFileTransfersQuery request, CancellationToken ct)
    {
        var allowed = await scope.GetAllowedRoomIdsAsync(ct);
        var query = db.FileTransfers.AsQueryable();

        if (request.Direction is { } direction)
            query = query.Where(t => t.Direction == direction);
        if (request.RoomId is { } roomId)
        {
            if (!await scope.CanAccessRoomAsync(roomId, ct))
                throw new ForbiddenException();
            query = query.Where(t => t.RoomId == roomId
                                     || (t.ComputerId != null && db.Computers.Any(c => c.Id == t.ComputerId && c.RoomId == roomId)));
        }
        else if (allowed is not null)
        {
            query = query.Where(t => (t.RoomId != null && allowed.Contains(t.RoomId.Value))
                                     || (t.ComputerId != null && db.Computers.Any(c => c.Id == t.ComputerId && allowed.Contains(c.RoomId))));
        }

        var rows = await query.OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync(ct);
        return await FileTransferMapping.ToSummariesAsync(db, rows, ct);
    }
}

public sealed record GetFileTransferQuery(int Id) : IRequest<FileTransferDetail>;

internal sealed class GetFileTransferHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetFileTransferQuery, FileTransferDetail>
{
    public async Task<FileTransferDetail> Handle(GetFileTransferQuery request, CancellationToken ct)
    {
        var transfer = await db.FileTransfers.FirstOrDefaultAsync(t => t.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(FileTransfer), request.Id);
        await FileTransferMapping.EnsureScopeAsync(db, scope, transfer, ct);

        var summary = (await FileTransferMapping.ToSummariesAsync(db, [transfer], ct))[0];

        var results = transfer.CommandId is { } commandId
            ? await db.CommandResults.Where(r => r.CommandId == commandId)
                .Select(r => new { r.ComputerId, ComputerName = r.Computer.Name, Status = r.Status.ToString(), r.ExecutedAt, r.ErrorMessage })
                .OrderBy(r => r.ComputerName).ToListAsync(ct)
            : [];
        var collected = await db.CollectedFiles.Where(c => c.FileTransferId == transfer.Id).ToDictionaryAsync(c => c.ComputerId, ct);

        var targets = results.Select(r =>
        {
            collected.TryGetValue(r.ComputerId, out var c);
            return new TransferTargetResult(r.ComputerId, r.ComputerName, r.Status, r.ExecutedAt, r.ErrorMessage,
                c?.SizeBytes, c?.FileCount, c?.Truncated);
        }).ToList();

        return new FileTransferDetail(summary, targets);
    }
}

public sealed record OpenCollectedFileQuery(int TransferId, int ComputerId) : IRequest<CollectedDownload>;

internal sealed class OpenCollectedFileHandler(
    IAppDbContext db, IRoomScope scope, ICurrentUser currentUser, IFileTransferStore store)
    : IRequestHandler<OpenCollectedFileQuery, CollectedDownload>
{
    public async Task<CollectedDownload> Handle(OpenCollectedFileQuery request, CancellationToken ct)
    {
        var transfer = await db.FileTransfers.FirstOrDefaultAsync(t => t.Id == request.TransferId && t.Direction == TransferDirection.Collect, ct)
            ?? throw new NotFoundException(nameof(FileTransfer), request.TransferId);

        var computer = await db.Computers.Where(c => c.Id == request.ComputerId).Select(c => new { c.Name, c.RoomId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Computer), request.ComputerId);
        if (!await scope.CanAccessRoomAsync(computer.RoomId, ct))
            throw new ForbiddenException();

        var stream = store.OpenCollected(request.TransferId, request.ComputerId)
            ?? throw new NotFoundException("Collected file", $"{request.TransferId}/{request.ComputerId}");

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "FileTransfer.Download",
            EntityType = nameof(FileTransfer),
            EntityId = request.TransferId.ToString(),
            Detail = JsonSerializer.Serialize(new { request.ComputerId, computerName = computer.Name }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        var safeName = string.Concat(computer.Name.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return new CollectedDownload(stream, $"thu-file-{request.TransferId}-{(safeName.Length == 0 ? request.ComputerId.ToString() : safeName)}.zip");
    }
}

internal static class FileTransferMapping
{
    public static async Task EnsureScopeAsync(IAppDbContext db, IRoomScope scope, FileTransfer transfer, CancellationToken ct)
    {
        var roomId = transfer.RoomId
            ?? (transfer.ComputerId is { } cid
                ? await db.Computers.Where(c => c.Id == cid).Select(c => (int?)c.RoomId).FirstOrDefaultAsync(ct)
                : null);
        if (roomId is { } r && !await scope.CanAccessRoomAsync(r, ct))
            throw new ForbiddenException();
    }

    public static async Task<IReadOnlyList<FileTransferSummary>> ToSummariesAsync(
        IAppDbContext db, IReadOnlyList<FileTransfer> rows, CancellationToken ct)
    {
        var roomIds = rows.Where(t => t.RoomId != null).Select(t => t.RoomId!.Value).Distinct().ToList();
        var computerIds = rows.Where(t => t.ComputerId != null).Select(t => t.ComputerId!.Value).Distinct().ToList();
        var userIds = rows.Where(t => t.CreatedById != null).Select(t => t.CreatedById!.Value).Distinct().ToList();

        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var computers = await db.Computers.Where(c => computerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return rows.Select(t => new FileTransferSummary(
            t.Id, t.Direction.ToString(), t.RoomId, t.RoomId is { } r && rooms.TryGetValue(r, out var rn) ? rn : null,
            t.ComputerId, t.ComputerId is { } c && computers.TryGetValue(c, out var cn) ? cn : null,
            t.FolderPath, t.FileName, t.SizeBytes, t.Filter, t.Overwrite, t.CommandId,
            t.CreatedById is { } u && users.TryGetValue(u, out var un) ? un : null, t.CreatedAt)).ToList();
    }
}
