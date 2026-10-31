using System.Text.Json;
using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Application.Common.Realtime;
using LabMonitoring.Contracts.Agent;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Commands;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Commands;

// --- Queries ---

public sealed record GetCommandsQuery(int? RoomId = null, int? ComputerId = null, int? SessionId = null)
    : IRequest<IReadOnlyList<CommandDto>>;

internal sealed class GetCommandsHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetCommandsQuery, IReadOnlyList<CommandDto>>
{
    public async Task<IReadOnlyList<CommandDto>> Handle(GetCommandsQuery request, CancellationToken ct)
    {
        var allowed = await scope.GetAllowedRoomIdsAsync(ct);

        var query = db.Commands.AsQueryable();
        if (allowed is not null)
            query = query.Where(c => (c.RoomId != null && allowed.Contains(c.RoomId.Value))
                                      || (c.ComputerId != null && allowed.Contains(c.Computer!.RoomId)));
        if (request.RoomId is { } roomId)
            query = query.Where(c => c.RoomId == roomId || (c.ComputerId != null && c.Computer!.RoomId == roomId));
        if (request.ComputerId is { } computerId)
            query = query.Where(c => c.ComputerId == computerId);
        if (request.SessionId is { } sessionId)
            query = query.Where(c => c.SessionId == sessionId);

        return await query
            .OrderByDescending(c => c.IssuedAt)
            .Take(200)
            .Select(c => new CommandDto(
                c.Id, c.ComputerId, c.Computer != null ? c.Computer.Name : null,
                c.RoomId, c.Room != null ? c.Room.Name : null, c.SessionId,
                c.CommandType.ToString(), c.Payload, c.IssuedById, c.IssuedBy.FullName, c.IssuedAt,
                c.Results.Select(r => new CommandResultDto(
                    r.Id, r.CommandId, r.ComputerId, r.Computer.Name, r.Status.ToString(), r.ExecutedAt, r.ErrorMessage))
                    .ToList()))
            .ToListAsync(ct);
    }
}

public sealed record GetCommandResultsQuery(long CommandId) : IRequest<IReadOnlyList<CommandResultDto>>;

internal sealed class GetCommandResultsHandler(IAppDbContext db, IRoomScope scope)
    : IRequestHandler<GetCommandResultsQuery, IReadOnlyList<CommandResultDto>>
{
    public async Task<IReadOnlyList<CommandResultDto>> Handle(GetCommandResultsQuery request, CancellationToken ct)
    {
        var command = await db.Commands.FirstOrDefaultAsync(c => c.Id == request.CommandId, ct)
            ?? throw new NotFoundException(nameof(Command), request.CommandId);

        var roomId = command.RoomId ?? await db.Computers
            .Where(c => c.Id == command.ComputerId)
            .Select(c => c.RoomId)
            .FirstAsync(ct);
        if (!await scope.CanAccessRoomAsync(roomId, ct))
            throw new ForbiddenException();

        return await db.CommandResults
            .Where(r => r.CommandId == request.CommandId)
            .OrderBy(r => r.Computer.Name)
            .Select(r => new CommandResultDto(r.Id, r.CommandId, r.ComputerId, r.Computer.Name, r.Status.ToString(), r.ExecutedAt, r.ErrorMessage))
            .ToListAsync(ct);
    }
}

// --- Command ---

/// <summary>
/// Issues a control command to one machine or every active/approved machine in a room (SRS FR-4).
/// Exactly one of <see cref="ComputerId"/>/<see cref="RoomId"/> must be set. Room-wide
/// Restart/Shutdown requires <see cref="Confirm"/> = true (FR-4.3's two-step confirmation — the
/// UI shows the dialog; the server just refuses to fan out a machine-room reboot without the flag).
/// </summary>
public sealed record IssueCommandCommand(
    int? ComputerId, int? RoomId, int? SessionId,
    CommandKind CommandType, string? Text, string? ExecutablePath, string? Url, bool Confirm,
    Guid? PresentationSessionId = null, bool LockInput = true, int? TransferId = null)
    : IRequest<IssueCommandResultDto>;

public sealed class IssueCommandValidator : AbstractValidator<IssueCommandCommand>
{
    public IssueCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => x.ComputerId.HasValue ^ x.RoomId.HasValue)
            .WithMessage("Specify exactly one of computerId or roomId.");

        // Presentation commands are issued only by LabHub (which owns the session) — never over REST.
        RuleFor(x => x.PresentationSessionId).NotNull()
            .When(x => x.CommandType is CommandKind.StartPresentation or CommandKind.StopPresentation)
            .WithMessage("Presentation commands are issued through the realtime hub.");
        RuleFor(x => x.RoomId).NotNull()
            .When(x => x.CommandType is CommandKind.StartPresentation or CommandKind.StopPresentation)
            .WithMessage("Presentation applies to a whole room.");

        // File-transfer commands are issued only by the file-transfer handlers, which own the FileTransfer row.
        RuleFor(x => x.TransferId).NotNull()
            .When(x => x.CommandType is CommandKind.PushFile or CommandKind.CollectFiles)
            .WithMessage("File-transfer commands are issued through the file-transfer API.");

        RuleFor(x => x.Text).NotEmpty().When(x => x.CommandType == CommandKind.Message)
            .WithMessage("Message text is required.");
        RuleFor(x => x.ExecutablePath).NotEmpty().When(x => x.CommandType == CommandKind.OpenApp)
            .WithMessage("Executable path is required.");
        RuleFor(x => x.Url).NotEmpty().When(x => x.CommandType == CommandKind.OpenUrl)
            .WithMessage("URL is required.");
        RuleFor(x => x.Url)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => x.CommandType == CommandKind.OpenUrl && !string.IsNullOrWhiteSpace(x.Url))
            .WithMessage("URL must be an absolute URL (e.g. https://example.com).");
    }
}

internal sealed class IssueCommandHandler(
    IAppDbContext db, IRoomScope scope, ICurrentUser currentUser, ICommandSigner signer, IRealtimeNotifier realtime)
    : IRequestHandler<IssueCommandCommand, IssueCommandResultDto>
{
    public async Task<IssueCommandResultDto> Handle(IssueCommandCommand request, CancellationToken ct)
    {
        var (targets, roomId) = await ResolveTargetsAsync(request, ct);
        var payloadJson = await BuildPayloadAsync(request, ct);

        if (request.CommandType is CommandKind.OpenApp or CommandKind.OpenUrl)
            await EnsureWhitelistedAsync(roomId, request, ct);

        var userId = currentUser.UserId ?? throw new ForbiddenException();
        var command = new Command
        {
            ComputerId = request.ComputerId,
            RoomId = request.RoomId,
            SessionId = request.SessionId,
            CommandType = request.CommandType,
            Payload = payloadJson,
            IssuedById = userId,
            IssuedAt = DateTime.UtcNow,
            Nonce = Guid.NewGuid(),
            // Representative signature for audit/debugging — each agent gets its OWN signature
            // (computed below, using its own AgentSecret) when the envelope is actually dispatched.
            Signature = await signer.SignForComputerAsync(targets[0].Id, payloadJson, ct),
        };
        db.Commands.Add(command);
        await db.SaveChangesAsync(ct); // assign command.Id before adding CommandResults / dispatching

        foreach (var target in targets)
            db.CommandResults.Add(new CommandResult { CommandId = command.Id, ComputerId = target.Id, Status = CommandResultStatus.Sent });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = $"Command.{request.CommandType}",
            EntityType = nameof(Command),
            EntityId = command.Id.ToString(),
            Detail = JsonSerializer.Serialize(new { targetComputerIds = targets.Select(t => t.Id), roomId, payload = payloadJson }),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        foreach (var target in targets)
        {
            var signature = target.Id == targets[0].Id
                ? command.Signature
                : await signer.SignForComputerAsync(target.Id, payloadJson, ct);

            var envelope = new CommandEnvelope
            {
                CommandId = command.Id,
                Type = MapType(request.CommandType),
                PayloadJson = payloadJson,
                IssuedAt = command.IssuedAt,
                Nonce = command.Nonce,
                Signature = signature,
            };
            await realtime.CommandIssuedAsync(target.Id, envelope, ct);
        }

        return new IssueCommandResultDto(command.Id, targets.Count);
    }

    private async Task<(List<Computer> Targets, int RoomId)> ResolveTargetsAsync(IssueCommandCommand request, CancellationToken ct)
    {
        if (request.ComputerId is { } computerId)
        {
            var computer = await db.Computers.FirstOrDefaultAsync(c => c.Id == computerId, ct)
                ?? throw new NotFoundException(nameof(Computer), computerId);
            if (!await scope.CanAccessRoomAsync(computer.RoomId, ct))
                throw new ForbiddenException();
            if (!computer.IsActive || !computer.IsApproved)
                throw new ConflictException("This machine is not active/approved.");

            return ([computer], computer.RoomId);
        }

        var roomId = request.RoomId!.Value;
        if (!await scope.CanAccessRoomAsync(roomId, ct))
            throw new ForbiddenException();
        if (!await db.Rooms.AnyAsync(r => r.Id == roomId, ct))
            throw new NotFoundException(nameof(Domain.Facilities.Room), roomId);

        var targets = await db.Computers.Where(c => c.RoomId == roomId && c.IsActive && c.IsApproved).ToListAsync(ct);
        if (targets.Count == 0)
            throw new ConflictException("There are no active, approved machines in this room.");

        if (request.CommandType is CommandKind.Restart or CommandKind.Shutdown or CommandKind.UpdateAgent or CommandKind.PushFile or CommandKind.CollectFiles && !request.Confirm)
            throw new ArgumentException(
                $"Restarting/shutting down/updating all {targets.Count} machine(s) in this room requires confirm=true (SRS FR-4.3).");

        return (targets, roomId);
    }

    private async Task EnsureWhitelistedAsync(int roomId, IssueCommandCommand request, CancellationToken ct)
    {
        if (request.CommandType == CommandKind.OpenApp)
        {
            var path = request.ExecutablePath!;
            var policies = await db.ApplicationPolicies
                .Where(p => (p.RoomId == null || p.RoomId == roomId) && p.Application.ExecutablePath == path)
                .Select(p => p.PolicyType)
                .ToListAsync(ct);

            if (policies.Count == 0 || policies.Contains(PolicyType.Block) || !policies.Contains(PolicyType.Allow))
                throw new ForbiddenException($"Application '{path}' is not in this room's whitelist (SRS FR-4.4).");
        }
        else
        {
            var domain = new Uri(request.Url!, UriKind.Absolute).Host.ToLowerInvariant();
            var policies = await db.WebsitePolicies
                .Where(p => (p.RoomId == null || p.RoomId == roomId)
                            && (p.Domain.ToLower() == domain || domain.EndsWith("." + p.Domain.ToLower())))
                .Select(p => p.PolicyType)
                .ToListAsync(ct);

            if (policies.Count == 0 || policies.Contains(PolicyType.Block) || !policies.Contains(PolicyType.Allow))
                throw new ForbiddenException($"URL domain '{domain}' is not in this room's whitelist (SRS FR-4.4).");
        }
    }

    private async Task<string> BuildPayloadAsync(IssueCommandCommand request, CancellationToken ct)
    {
        switch (request.CommandType)
        {
            case CommandKind.Message:
                return JsonSerializer.Serialize(new { text = request.Text });
            case CommandKind.OpenApp:
                return JsonSerializer.Serialize(new { executablePath = request.ExecutablePath });
            case CommandKind.OpenUrl:
                return JsonSerializer.Serialize(new { url = request.Url });
            case CommandKind.UpdateAgent:
                var latest = await db.AgentPackages
                    .OrderByDescending(p => p.UploadedAt)
                    .Select(p => new { p.Version, p.Sha256 })
                    .FirstOrDefaultAsync(ct)
                    ?? throw new ConflictException("No Agent package has been uploaded yet.");
                return JsonSerializer.Serialize(new { version = latest.Version, sha256 = latest.Sha256 });
            case CommandKind.PushFile:
            case CommandKind.CollectFiles:
                var t = await db.FileTransfers.FirstOrDefaultAsync(x => x.Id == request.TransferId, ct)
                    ?? throw new NotFoundException(nameof(Domain.Transfers.FileTransfer), request.TransferId!);
                return request.CommandType == CommandKind.PushFile
                    ? JsonSerializer.Serialize(new
                    {
                        transferId = t.Id, fileName = t.FileName, sizeBytes = t.SizeBytes, sha256 = t.Sha256,
                        folderId = t.TransferFolderId, folderPath = t.FolderPath, overwrite = t.Overwrite,
                    })
                    : JsonSerializer.Serialize(new
                    {
                        transferId = t.Id, folderId = t.TransferFolderId, folderPath = t.FolderPath,
                        filter = t.Filter, maxBytes = t.MaxBytes,
                    });
            case CommandKind.StartPresentation:
                return JsonSerializer.Serialize(new { sessionId = request.PresentationSessionId, lockInput = request.LockInput });
            case CommandKind.StopPresentation:
                return JsonSerializer.Serialize(new { sessionId = request.PresentationSessionId });
            default:
                return "{}";
        }
    }

    private static CommandType MapType(CommandKind kind) => kind switch
    {
        CommandKind.Lock => CommandType.Lock,
        CommandKind.Restart => CommandType.Restart,
        CommandKind.Shutdown => CommandType.Shutdown,
        CommandKind.Message => CommandType.Message,
        CommandKind.OpenApp => CommandType.OpenApp,
        CommandKind.OpenUrl => CommandType.OpenUrl,
        CommandKind.UpdateAgent => CommandType.UpdateAgent,
        CommandKind.StartPresentation => CommandType.StartPresentation,
        CommandKind.StopPresentation => CommandType.StopPresentation,
        CommandKind.PushFile => CommandType.PushFile,
        CommandKind.CollectFiles => CommandType.CollectFiles,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
