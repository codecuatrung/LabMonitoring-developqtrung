using System.Text.Json;
using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Facilities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Commands;

public sealed record WakeResultDto(int TargetCount, int SentCount, IReadOnlyList<int> SkippedComputerIds);

/// <summary>
/// Wakes one machine or every active/approved machine in a room that's fully powered off
/// (SRS FR-4 extension, "khởi động máy đồng loạt"). Not a signed Agent command — sends a raw
/// Wake-on-LAN broadcast per target MAC address, so a machine with no recorded MAC is skipped
/// rather than failing the whole batch.
/// </summary>
public sealed record WakeComputersCommand(int? ComputerId, int? RoomId) : IRequest<WakeResultDto>;

public sealed class WakeComputersValidator : AbstractValidator<WakeComputersCommand>
{
    public WakeComputersValidator()
    {
        RuleFor(x => x)
            .Must(x => x.ComputerId.HasValue ^ x.RoomId.HasValue)
            .WithMessage("Specify exactly one of computerId or roomId.");
    }
}

internal sealed class WakeComputersHandler(IAppDbContext db, IRoomScope scope, ICurrentUser currentUser, IWakeOnLanSender wol)
    : IRequestHandler<WakeComputersCommand, WakeResultDto>
{
    public async Task<WakeResultDto> Handle(WakeComputersCommand request, CancellationToken ct)
    {
        var (targets, roomId) = await ResolveTargetsAsync(request, ct);

        var skipped = new List<int>();
        var sent = 0;
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.MacAddress))
            {
                skipped.Add(target.Id);
                continue;
            }
            await wol.SendAsync(target.MacAddress, ct);
            sent++;
        }

        var userId = currentUser.UserId ?? throw new ForbiddenException();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "Computer.WakeOnLan",
            EntityType = nameof(Computer),
            EntityId = request.ComputerId?.ToString(),
            Detail = JsonSerializer.Serialize(new { targetComputerIds = targets.Select(t => t.Id), roomId, sent, skipped }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return new WakeResultDto(targets.Count, sent, skipped);
    }

    private async Task<(List<Computer> Targets, int RoomId)> ResolveTargetsAsync(WakeComputersCommand request, CancellationToken ct)
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

        return (targets, roomId);
    }
}
