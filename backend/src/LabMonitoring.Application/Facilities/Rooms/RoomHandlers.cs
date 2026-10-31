using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Facilities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LabMonitoring.Application.Facilities.Rooms;

// --- Queries ---

/// <summary>Rooms the caller may see (all for Administrator, scoped otherwise — SRS FR-8.2).</summary>
public sealed record GetRoomsQuery(int? BuildingId = null, int Page = 1, int PageSize = 20) : IRequest<PagedResult<RoomDto>>;

internal sealed class GetRoomsHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetRoomsQuery, PagedResult<RoomDto>>
{
    public async Task<PagedResult<RoomDto>> Handle(GetRoomsQuery request, CancellationToken ct)
    {
        var allowed = await scope.GetAllowedRoomIdsAsync(ct);

        var query = db.Rooms.AsQueryable();
        if (request.BuildingId is { } bid)
            query = query.Where(r => r.BuildingId == bid);
        if (allowed is not null)
            query = query.Where(r => allowed.Contains(r.Id));

        query = query.OrderBy(r => r.Building.Name).ThenBy(r => r.Name);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RoomDto(r.Id, r.BuildingId, r.Building.Name, r.Name, r.Code,
                r.Capacity, r.MapImageUrl, r.Computers.Count,
                // Live status mix of the room's approved machines (drives the room cards' status bar).
                r.Computers.Count(c => c.IsApproved && c.IsActive && c.Status != null && c.Status.CurrentStatus == Domain.Common.ComputerState.Online),
                r.Computers.Count(c => c.IsApproved && c.IsActive && c.Status != null && c.Status.CurrentStatus == Domain.Common.ComputerState.InUse),
                r.Computers.Count(c => c.IsApproved && c.IsActive && c.Status != null && c.Status.CurrentStatus == Domain.Common.ComputerState.Warning),
                r.Computers.Count(c => !c.IsApproved)))
            .ToListAsync(ct);

        return new PagedResult<RoomDto>(items, total, page, pageSize);
    }
}

public sealed record GetRoomQuery(int Id) : IRequest<RoomDetailDto>;

internal sealed class GetRoomHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetRoomQuery, RoomDetailDto>
{
    public async Task<RoomDetailDto> Handle(GetRoomQuery request, CancellationToken ct)
    {
        if (!await scope.CanAccessRoomAsync(request.Id, ct))
            throw new ForbiddenException();

        var room = await db.Rooms
            .Where(r => r.Id == request.Id)
            .Select(r => new RoomDetailDto(r.Id, r.Name, r.Code, r.Capacity, r.MapImageUrl,
                r.BuildingId, r.Building.Name,
                r.Computers.OrderBy(c => c.Name).Select(c => new RoomComputerDto(
                    c.Id, c.Name,
                    c.Status != null ? c.Status.CurrentStatus.ToString() : "Offline",
                    c.IsApproved, c.IsActive, c.PositionX, c.PositionY,
                    c.Status != null ? c.Status.CurrentWindowsUser : null,
                    c.Metrics.OrderByDescending(m => m.RecordedAt).Select(m => m.CpuUsagePercent).FirstOrDefault(),
                    c.Metrics.OrderByDescending(m => m.RecordedAt).Select(m => m.RamUsagePercent).FirstOrDefault(),
                    c.Status != null ? c.Status.LastHeartbeatAt : null)).ToList()))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Room), request.Id);

        return room;
    }
}

// --- Commands ---

public sealed record CreateRoomCommand(int BuildingId, string Name, string Code, int Capacity, string? MapImageUrl)
    : IRequest<int>;

public sealed class CreateRoomValidator : AbstractValidator<CreateRoomCommand>
{
    public CreateRoomValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 500);
        RuleFor(x => x.MapImageUrl).MaximumLength(300);
    }
}

internal sealed class CreateRoomHandler(IAppDbContext db) : IRequestHandler<CreateRoomCommand, int>
{
    public async Task<int> Handle(CreateRoomCommand request, CancellationToken ct)
    {
        if (!await db.Buildings.AnyAsync(b => b.Id == request.BuildingId, ct))
            throw new NotFoundException(nameof(Building), request.BuildingId);
        if (await db.Rooms.AnyAsync(r => r.Code == request.Code, ct))
            throw new ConflictException($"Room code '{request.Code}' is already in use.");

        var room = new Room
        {
            BuildingId = request.BuildingId,
            Name = request.Name.Trim(),
            Code = request.Code.Trim(),
            Capacity = request.Capacity,
            MapImageUrl = string.IsNullOrWhiteSpace(request.MapImageUrl) ? null : request.MapImageUrl.Trim(),
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return room.Id;
    }
}

public sealed record UpdateRoomCommand(int Id, string Name, string Code, int Capacity, string? MapImageUrl) : IRequest;

public sealed class UpdateRoomValidator : AbstractValidator<UpdateRoomCommand>
{
    public UpdateRoomValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 500);
        RuleFor(x => x.MapImageUrl).MaximumLength(300);
    }
}

internal sealed class UpdateRoomHandler(IAppDbContext db) : IRequestHandler<UpdateRoomCommand>
{
    public async Task Handle(UpdateRoomCommand request, CancellationToken ct)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Room), request.Id);

        if (await db.Rooms.AnyAsync(r => r.Code == request.Code && r.Id != request.Id, ct))
            throw new ConflictException($"Room code '{request.Code}' is already in use.");

        room.Name = request.Name.Trim();
        room.Code = request.Code.Trim();
        room.Capacity = request.Capacity;
        room.MapImageUrl = string.IsNullOrWhiteSpace(request.MapImageUrl) ? null : request.MapImageUrl.Trim();
        await db.SaveChangesAsync(ct);
    }
}

public sealed record DeleteRoomCommand(int Id) : IRequest;

internal sealed class DeleteRoomHandler(IAppDbContext db) : IRequestHandler<DeleteRoomCommand>
{
    public async Task Handle(DeleteRoomCommand request, CancellationToken ct)
    {
        var room = await db.Rooms.Include(r => r.Computers).FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Room), request.Id);

        if (room.Computers.Count > 0)
            throw new ConflictException("Remove or move the computers in this room first.");

        db.Rooms.Remove(room);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Purges resource-history rows for every machine in a room at once — the room-level
/// counterpart of <see cref="LabMonitoring.Application.Facilities.Computers.DeleteComputerMetricsCommand"/>,
/// for clearing out a whole lab's history in one action instead of machine by machine.
/// <paramref name="Before"/> null means delete everything recorded for the room's computers.</summary>
public sealed record DeleteRoomMetricsCommand(int Id, DateTime? Before) : IRequest<int>;

internal sealed class DeleteRoomMetricsHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<DeleteRoomMetricsCommand, int>
{
    public async Task<int> Handle(DeleteRoomMetricsCommand request, CancellationToken ct)
    {
        if (!await db.Rooms.AnyAsync(r => r.Id == request.Id, ct))
            throw new NotFoundException(nameof(Room), request.Id);

        var query = db.ComputerMetrics.Where(m => m.Computer.RoomId == request.Id);
        if (request.Before is { } before)
            query = query.Where(m => m.RecordedAt < before);

        var deleted = await query.ExecuteDeleteAsync(ct);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "RoomMetrics.Delete",
            EntityType = nameof(Room),
            EntityId = request.Id.ToString(),
            Detail = JsonSerializer.Serialize(new { deletedCount = deleted, request.Before }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return deleted;
    }
}

/// <summary>
/// Rotates a room's registration code (SRS FR-8.1 "quản lý mã đăng ký phòng"): the Agent uses
/// <c>Rooms.Code</c> as the shared secret at self-registration (<c>RoomHandlers</c> above already
/// lets an admin set it to anything via Update; this generates an unguessable one on demand, e.g.
/// after a code leaks or before a bulk rollout to a room). Already-registered computers are
/// unaffected — they authenticate with their own AgentToken, never the room code again.
/// </summary>
public sealed record RegenerateRoomCodeCommand(int Id) : IRequest<string>;

internal sealed class RegenerateRoomCodeHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<RegenerateRoomCodeCommand, string>
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O/1/I/L

    public async Task<string> Handle(RegenerateRoomCodeCommand request, CancellationToken ct)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Room), request.Id);

        string code;
        do
        {
            code = NewCode();
        } while (await db.Rooms.AnyAsync(r => r.Code == code, ct));

        room.Code = code;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Room.RegenerateCode",
            EntityType = nameof(Room),
            EntityId = room.Id.ToString(),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return code;
    }

    private static string NewCode()
        => new(Enumerable.Range(0, 8).Select(_ => Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
}
