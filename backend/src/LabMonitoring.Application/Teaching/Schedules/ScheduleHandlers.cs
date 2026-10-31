using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Teaching;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Teaching.Schedules;

// --- Queries ---

public sealed record GetSchedulesQuery(int? ClassId = null, int? RoomId = null) : IRequest<IReadOnlyList<ScheduleDto>>;

internal sealed class GetSchedulesHandler(IAppDbContext db) : IRequestHandler<GetSchedulesQuery, IReadOnlyList<ScheduleDto>>
{
    public async Task<IReadOnlyList<ScheduleDto>> Handle(GetSchedulesQuery request, CancellationToken ct)
    {
        var query = db.Schedules.AsQueryable();
        if (request.ClassId is { } classId) query = query.Where(s => s.ClassId == classId);
        if (request.RoomId is { } roomId) query = query.Where(s => s.RoomId == roomId);

        return await query
            .OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
            .Select(s => new ScheduleDto(s.Id, s.ClassId, s.Class.Name, s.RoomId, s.Room.Name,
                s.DayOfWeek, s.StartTime, s.EndTime, s.EffectiveFrom, s.EffectiveTo))
            .ToListAsync(ct);
    }
}

// --- Commands ---

public sealed record CreateScheduleCommand(
    int ClassId, int RoomId, byte DayOfWeek, TimeOnly StartTime, TimeOnly EndTime,
    DateOnly EffectiveFrom, DateOnly EffectiveTo) : IRequest<int>;

public sealed class CreateScheduleValidator : AbstractValidator<CreateScheduleCommand>
{
    public CreateScheduleValidator()
    {
        RuleFor(x => x.DayOfWeek).InclusiveBetween((byte)0, (byte)6);
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).WithMessage("End time must be after start time.");
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom);
    }
}

internal sealed class CreateScheduleHandler(IAppDbContext db) : IRequestHandler<CreateScheduleCommand, int>
{
    public async Task<int> Handle(CreateScheduleCommand request, CancellationToken ct)
    {
        if (!await db.Classes.AnyAsync(c => c.Id == request.ClassId, ct))
            throw new NotFoundException(nameof(Class), request.ClassId);
        if (!await db.Rooms.AnyAsync(r => r.Id == request.RoomId, ct))
            throw new NotFoundException(nameof(Room), request.RoomId);

        var overlaps = await db.Schedules.AnyAsync(s =>
            s.RoomId == request.RoomId && s.DayOfWeek == request.DayOfWeek
            && s.EffectiveFrom <= request.EffectiveTo && s.EffectiveTo >= request.EffectiveFrom
            && s.StartTime < request.EndTime && request.StartTime < s.EndTime, ct);
        if (overlaps)
            throw new ConflictException("This room is already scheduled for an overlapping time slot.");

        var entity = new Schedule
        {
            ClassId = request.ClassId,
            RoomId = request.RoomId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
        };
        db.Schedules.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }
}

public sealed record DeleteScheduleCommand(int Id) : IRequest;

internal sealed class DeleteScheduleHandler(IAppDbContext db) : IRequestHandler<DeleteScheduleCommand>
{
    public async Task Handle(DeleteScheduleCommand request, CancellationToken ct)
    {
        var entity = await db.Schedules.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Schedule), request.Id);

        db.Schedules.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
