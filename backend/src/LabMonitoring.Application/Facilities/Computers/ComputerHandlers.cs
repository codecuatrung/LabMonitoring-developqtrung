using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Application.Common.Realtime;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LabMonitoring.Application.Facilities.Computers;

// --- Queries ---

public sealed record GetComputersQuery(int? RoomId = null, string? Status = null, bool? PendingApproval = null)
    : IRequest<IReadOnlyList<RoomComputerDto>>;

internal sealed class GetComputersHandler(IAppDbContext db, IRoomScope scope)
    : IRequestHandler<GetComputersQuery, IReadOnlyList<RoomComputerDto>>
{
    public async Task<IReadOnlyList<RoomComputerDto>> Handle(GetComputersQuery request, CancellationToken ct)
    {
        var allowed = await scope.GetAllowedRoomIdsAsync(ct);

        var query = db.Computers.AsQueryable();
        if (allowed is not null)
            query = query.Where(c => allowed.Contains(c.RoomId));
        if (request.RoomId is { } roomId)
            query = query.Where(c => c.RoomId == roomId);
        if (request.PendingApproval == true)
            query = query.Where(c => !c.IsApproved);
        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<ComputerState>(request.Status, ignoreCase: true, out var state))
            query = query.Where(c => c.Status != null && c.Status.CurrentStatus == state);

        return await query
            .OrderBy(c => c.Room.Name).ThenBy(c => c.Name)
            .Select(c => new RoomComputerDto(
                c.Id, c.Name,
                c.Status != null ? c.Status.CurrentStatus.ToString() : "Offline",
                c.IsApproved, c.IsActive, c.PositionX, c.PositionY,
                c.Status != null ? c.Status.CurrentWindowsUser : null,
                c.Metrics.OrderByDescending(m => m.RecordedAt).Select(m => m.CpuUsagePercent).FirstOrDefault(),
                c.Metrics.OrderByDescending(m => m.RecordedAt).Select(m => m.RamUsagePercent).FirstOrDefault(),
                c.Status != null ? c.Status.LastHeartbeatAt : null))
            .ToListAsync(ct);
    }
}

public sealed record GetComputerQuery(int Id) : IRequest<ComputerDetailDto>;

internal sealed class GetComputerHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetComputerQuery, ComputerDetailDto>
{
    public async Task<ComputerDetailDto> Handle(GetComputerQuery request, CancellationToken ct)
    {
        var computer = await db.Computers
            .Where(c => c.Id == request.Id)
            .Select(c => new
            {
                c.Id, c.RoomId, RoomName = c.Room.Name, c.Name, c.MachineId,
                c.IsApproved, c.IsActive, c.MacAddress, c.PositionX, c.PositionY, c.AgentVersion, c.CreatedAt,
                Status = c.Status, Spec = c.Spec,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Computer), request.Id);

        if (!await scope.CanAccessRoomAsync(computer.RoomId, ct))
            throw new ForbiddenException();

        return new ComputerDetailDto(
            computer.Id, computer.RoomId, computer.RoomName, computer.Name, computer.MachineId,
            computer.Status?.CurrentStatus.ToString() ?? "Offline", computer.IsApproved, computer.IsActive,
            computer.MacAddress, computer.PositionX, computer.PositionY, computer.AgentVersion, computer.CreatedAt,
            computer.Status?.CurrentWindowsUser, computer.Status?.LastIpAddress, computer.Status?.LastHeartbeatAt,
            computer.Spec is null ? null : new ComputerSpecDto(
                computer.Spec.CpuModel, computer.Spec.CpuCores, computer.Spec.RamTotalMB,
                computer.Spec.DiskTotalGB, computer.Spec.OsVersion, computer.Spec.UpdatedAt));
    }
}

public sealed record GetComputerMetricsQuery(int Id, DateTime? From, DateTime? To, int Page = 1, int PageSize = 30)
    : IRequest<PagedResult<MetricPointDto>>;

internal sealed class GetComputerMetricsHandler(IAppDbContext db, IRoomScope scope)
    : IRequestHandler<GetComputerMetricsQuery, PagedResult<MetricPointDto>>
{
    public async Task<PagedResult<MetricPointDto>> Handle(GetComputerMetricsQuery request, CancellationToken ct)
    {
        var roomId = await db.Computers.Where(c => c.Id == request.Id).Select(c => (int?)c.RoomId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Computer), request.Id);
        if (!await scope.CanAccessRoomAsync(roomId, ct))
            throw new ForbiddenException();

        var query = db.ComputerMetrics.Where(m => m.ComputerId == request.Id);
        if (request.From is { } from)
            query = query.Where(m => m.RecordedAt >= from);
        if (request.To is { } to)
            query = query.Where(m => m.RecordedAt <= to);

        var total = await query.CountAsync(ct);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var items = await query
            .OrderByDescending(m => m.RecordedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MetricPointDto(m.RecordedAt, m.CpuUsagePercent, m.RamUsagePercent, m.DiskUsagePercent, m.TemperatureC))
            .ToListAsync(ct);

        return new PagedResult<MetricPointDto>(items, total, page, pageSize);
    }
}

// --- Commands ---

/// <summary>Purges resource-history rows for one machine (an on-demand cleanup, distinct from the
/// automatic retention/rollup job planned for M5 — see <see cref="LabMonitoring.Domain.Facilities.ComputerMetric"/>).
/// <paramref name="Before"/> null means delete everything recorded for this computer.</summary>
public sealed record DeleteComputerMetricsCommand(int Id, DateTime? Before) : IRequest<int>;

internal sealed class DeleteComputerMetricsHandler(IAppDbContext db, IRoomScope scope, ICurrentUser currentUser)
    : IRequestHandler<DeleteComputerMetricsCommand, int>
{
    public async Task<int> Handle(DeleteComputerMetricsCommand request, CancellationToken ct)
    {
        var roomId = await db.Computers.Where(c => c.Id == request.Id).Select(c => (int?)c.RoomId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Computer), request.Id);
        if (!await scope.CanAccessRoomAsync(roomId, ct))
            throw new ForbiddenException();

        var query = db.ComputerMetrics.Where(m => m.ComputerId == request.Id);
        if (request.Before is { } before)
            query = query.Where(m => m.RecordedAt < before);

        var deleted = await query.ExecuteDeleteAsync(ct);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "ComputerMetrics.Delete",
            EntityType = nameof(Computer),
            EntityId = request.Id.ToString(),
            Detail = JsonSerializer.Serialize(new { deletedCount = deleted, request.Before }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return deleted;
    }
}

public sealed record UpdateComputerCommand(int Id, string Name, int? PositionX, int? PositionY, bool IsActive) : IRequest;

public sealed class UpdateComputerValidator : AbstractValidator<UpdateComputerCommand>
{
    public UpdateComputerValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
}

internal sealed class UpdateComputerHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<UpdateComputerCommand>
{
    public async Task Handle(UpdateComputerCommand request, CancellationToken ct)
    {
        var computer = await db.Computers.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Computer), request.Id);
        if (!await scope.CanAccessRoomAsync(computer.RoomId, ct))
            throw new ForbiddenException();

        computer.Name = request.Name.Trim();
        computer.PositionX = request.PositionX;
        computer.PositionY = request.PositionY;
        computer.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Approve a self-registered machine (SRS FR-5.3).</summary>
public sealed record ApproveComputerCommand(int Id) : IRequest;

internal sealed class ApproveComputerHandler(IAppDbContext db, IRoomScope scope, IRealtimeNotifier realtime)
    : IRequestHandler<ApproveComputerCommand>
{
    public async Task Handle(ApproveComputerCommand request, CancellationToken ct)
    {
        var computer = await db.Computers.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Computer), request.Id);
        if (!await scope.CanAccessRoomAsync(computer.RoomId, ct))
            throw new ForbiddenException();

        if (computer.IsApproved)
            throw new ConflictException("This computer is already approved.");

        computer.IsApproved = true;
        computer.IsActive = true;
        await db.SaveChangesAsync(ct);

        await realtime.ComputerStatusChangedAsync(new ComputerStatusChangedEvent(
            computer.Id, computer.RoomId, computer.Status?.CurrentStatus.ToString() ?? "Offline", null, null), ct);
    }
}

/// <summary>Reject (and remove) a self-registered machine.</summary>
public sealed record RejectComputerCommand(int Id) : IRequest;

internal sealed class RejectComputerHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<RejectComputerCommand>
{
    public async Task Handle(RejectComputerCommand request, CancellationToken ct)
    {
        var computer = await db.Computers.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Computer), request.Id);
        if (!await scope.CanAccessRoomAsync(computer.RoomId, ct))
            throw new ForbiddenException();
        if (computer.IsApproved)
            throw new ConflictException("Cannot reject an already-approved computer; delete it instead.");

        db.Computers.Remove(computer);
        await db.SaveChangesAsync(ct);
    }
}

public sealed record DeleteComputerCommand(int Id) : IRequest;

internal sealed class DeleteComputerHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<DeleteComputerCommand>
{
    public async Task Handle(DeleteComputerCommand request, CancellationToken ct)
    {
        var computer = await db.Computers.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Computer), request.Id);
        if (!await scope.CanAccessRoomAsync(computer.RoomId, ct))
            throw new ForbiddenException();

        db.Computers.Remove(computer);
        await db.SaveChangesAsync(ct);
    }
}
