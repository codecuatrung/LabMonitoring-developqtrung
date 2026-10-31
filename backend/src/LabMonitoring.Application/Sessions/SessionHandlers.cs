using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Sessions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Sessions;

// --- Queries ---

public sealed record GetSessionsQuery(int? RoomId = null, int? ClassId = null, string? Status = null, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<SessionDto>>;

internal sealed class GetSessionsHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetSessionsQuery, PagedResult<SessionDto>>
{
    public async Task<PagedResult<SessionDto>> Handle(GetSessionsQuery request, CancellationToken ct)
    {
        var allowed = await scope.GetAllowedRoomIdsAsync(ct);

        var query = db.Sessions.AsQueryable();
        if (allowed is not null) query = query.Where(s => allowed.Contains(s.RoomId));
        if (request.RoomId is { } roomId) query = query.Where(s => s.RoomId == roomId);
        if (request.ClassId is { } classId) query = query.Where(s => s.ClassId == classId);
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<SessionStatus>(request.Status, true, out var status))
            query = query.Where(s => s.Status == status);

        query = query.OrderByDescending(s => s.StartedAt);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SessionDto(s.Id, s.RoomId, s.Room.Name, s.ClassId, s.Class.Name,
                s.TeacherId, s.Teacher.FullName, s.StartedAt, s.EndedAt, s.Status.ToString(),
                s.Computers.Count, s.Computers.Count(c => c.StudentId != null)))
            .ToListAsync(ct);

        return new PagedResult<SessionDto>(items, total, page, pageSize);
    }
}

public sealed record GetSessionQuery(int Id) : IRequest<SessionDetailDto>;

internal sealed class GetSessionHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetSessionQuery, SessionDetailDto>
{
    public async Task<SessionDetailDto> Handle(GetSessionQuery request, CancellationToken ct)
    {
        var session = await db.Sessions
            .Where(s => s.Id == request.Id)
            .Select(s => new SessionDetailDto(
                s.Id, s.RoomId, s.Room.Name, s.ClassId, s.Class.Name, s.TeacherId, s.Teacher.FullName,
                s.StartedAt, s.EndedAt, s.Status.ToString(),
                s.Computers.OrderBy(c => c.Computer.Name).Select(c => new SessionComputerDto(
                    c.ComputerId, c.Computer.Name, c.StudentId,
                    c.Student != null ? c.Student.StudentCode : null,
                    c.Student != null ? c.Student.FullName : null,
                    c.LoginAt, c.LogoutAt)).ToList()))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Session), request.Id);

        if (!await scope.CanAccessRoomAsync(session.RoomId, ct))
            throw new ForbiddenException();

        return session;
    }
}

public sealed record GetSessionReportQuery(int Id) : IRequest<SessionReportDto>;

internal sealed class GetSessionReportHandler(IAppDbContext db, IRoomScope scope) : IRequestHandler<GetSessionReportQuery, SessionReportDto>
{
    public async Task<SessionReportDto> Handle(GetSessionReportQuery request, CancellationToken ct)
    {
        var session = await db.Sessions.Include(s => s.Class).ThenInclude(c => c.ClassStudents)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Session), request.Id);

        if (!await scope.CanAccessRoomAsync(session.RoomId, ct))
            throw new ForbiddenException();

        var roomName = await db.Rooms.Where(r => r.Id == session.RoomId).Select(r => r.Name).FirstAsync(ct);
        var teacherName = await db.Users.Where(u => u.Id == session.TeacherId).Select(u => u.FullName).FirstAsync(ct);

        var computers = await db.SessionComputers.Where(c => c.SessionId == request.Id)
            .OrderBy(c => c.Computer.Name)
            .Select(c => new SessionComputerDto(c.ComputerId, c.Computer.Name, c.StudentId,
                c.Student != null ? c.Student.StudentCode : null, c.Student != null ? c.Student.FullName : null,
                c.LoginAt, c.LogoutAt))
            .ToListAsync(ct);

        var end = session.EndedAt ?? DateTime.UtcNow;
        return new SessionReportDto(
            session.Id, session.Class.Name, roomName, teacherName, session.StartedAt, session.EndedAt,
            (end - session.StartedAt).TotalMinutes,
            ComputersUsed: computers.Count(c => c.LoginAt != null),
            StudentsLoggedIn: computers.Count(c => c.LoginAt != null),
            StudentsEnrolled: session.Class.ClassStudents.Count,
            Computers: computers);
    }
}

// --- Commands ---

/// <summary>Begin a lab session for a class in a room (SRS FR-6.2). Also seeds a SessionComputers row
/// for every active, approved machine in the room so heartbeat login/logout tracking (FR-2.2) has
/// somewhere to write; allocation (FR-6.3) then fills in which student sits where.</summary>
public sealed record StartSessionCommand(int RoomId, int ClassId, int? ScheduleId) : IRequest<int>;

public sealed class StartSessionValidator : AbstractValidator<StartSessionCommand>
{
    public StartSessionValidator()
    {
        RuleFor(x => x.RoomId).GreaterThan(0);
        RuleFor(x => x.ClassId).GreaterThan(0);
    }
}

internal sealed class StartSessionHandler(IAppDbContext db, IRoomScope scope, ICurrentUser currentUser)
    : IRequestHandler<StartSessionCommand, int>
{
    public async Task<int> Handle(StartSessionCommand request, CancellationToken ct)
    {
        if (!await scope.CanAccessRoomAsync(request.RoomId, ct))
            throw new ForbiddenException();

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == request.ClassId, ct)
            ?? throw new NotFoundException("Class", request.ClassId);
        if (!await db.Rooms.AnyAsync(r => r.Id == request.RoomId, ct))
            throw new NotFoundException("Room", request.RoomId);

        var teacherId = currentUser.UserId ?? throw new ForbiddenException();
        if (!currentUser.IsAdministrator && cls.TeacherId != teacherId)
            throw new ForbiddenException("Only the class's teacher or an administrator can start this session.");

        if (await db.Sessions.AnyAsync(s => s.RoomId == request.RoomId && s.Status == SessionStatus.Active, ct))
            throw new ConflictException("This room already has an active session.");

        var session = new Session
        {
            ScheduleId = request.ScheduleId,
            RoomId = request.RoomId,
            ClassId = request.ClassId,
            TeacherId = currentUser.IsAdministrator ? cls.TeacherId : teacherId,
            StartedAt = DateTime.UtcNow,
            Status = SessionStatus.Active,
        };
        db.Sessions.Add(session);

        var computerIds = await db.Computers
            .Where(c => c.RoomId == request.RoomId && c.IsActive && c.IsApproved)
            .Select(c => c.Id)
            .ToListAsync(ct);

        await db.SaveChangesAsync(ct); // assign session.Id before adding children

        foreach (var computerId in computerIds)
            db.SessionComputers.Add(new SessionComputer { SessionId = session.Id, ComputerId = computerId });

        await db.SaveChangesAsync(ct);
        return session.Id;
    }
}

public sealed record EndSessionCommand(int Id) : IRequest;

internal sealed class EndSessionHandler(IAppDbContext db, IRoomScope scope, ICurrentUser currentUser)
    : IRequestHandler<EndSessionCommand>
{
    public async Task Handle(EndSessionCommand request, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Session), request.Id);
        if (!await scope.CanAccessRoomAsync(session.RoomId, ct))
            throw new ForbiddenException();
        if (!currentUser.IsAdministrator && session.TeacherId != currentUser.UserId)
            throw new ForbiddenException("Only the teacher who started this session or an administrator can end it.");
        if (session.Status != SessionStatus.Active)
            throw new ConflictException("This session is not active.");

        var now = DateTime.UtcNow;
        session.Status = SessionStatus.Ended;
        session.EndedAt = now;

        // Auto-close any workstation that never reported a logout.
        await db.SessionComputers
            .Where(c => c.SessionId == session.Id && c.LoginAt != null && c.LogoutAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LogoutAt, now), ct);

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Assigns enrolled students to the session's workstations, automatically (by code order)
/// or from an explicit list (SRS FR-6.3).</summary>
public sealed record StudentAssignment(int ComputerId, int? StudentId);
public sealed record AllocateStudentsCommand(int SessionId, bool Auto, IReadOnlyList<StudentAssignment>? Assignments) : IRequest;

internal sealed class AllocateStudentsHandler(IAppDbContext db, IRoomScope scope, ICurrentUser currentUser)
    : IRequestHandler<AllocateStudentsCommand>
{
    public async Task Handle(AllocateStudentsCommand request, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == request.SessionId, ct)
            ?? throw new NotFoundException(nameof(Session), request.SessionId);
        if (!await scope.CanAccessRoomAsync(session.RoomId, ct))
            throw new ForbiddenException();
        if (!currentUser.IsAdministrator && session.TeacherId != currentUser.UserId)
            throw new ForbiddenException("Only the session's teacher or an administrator can allocate students.");
        if (session.Status != SessionStatus.Active)
            throw new ConflictException("Only an active session can be allocated.");

        var sessionComputers = await db.SessionComputers
            .Where(c => c.SessionId == request.SessionId)
            .OrderBy(c => c.Computer.Name)
            .ToListAsync(ct);

        if (request.Auto)
        {
            var alreadyAssigned = sessionComputers.Where(c => c.StudentId != null).Select(c => c.StudentId!.Value).ToHashSet();
            var students = await db.ClassStudents.Where(cs => cs.ClassId == session.ClassId)
                .OrderBy(cs => cs.Student.StudentCode)
                .Select(cs => cs.StudentId)
                .Where(id => !alreadyAssigned.Contains(id))
                .ToListAsync(ct);

            var freeComputers = sessionComputers.Where(c => c.StudentId is null).ToList();
            foreach (var (computer, studentId) in freeComputers.Zip(students))
                computer.StudentId = studentId;
        }
        else
        {
            var byComputer = sessionComputers.ToDictionary(c => c.ComputerId);
            foreach (var a in request.Assignments ?? [])
            {
                if (!byComputer.TryGetValue(a.ComputerId, out var computer))
                    throw new NotFoundException("SessionComputer", a.ComputerId);
                if (a.StudentId is { } sid && !await db.ClassStudents.AnyAsync(cs => cs.ClassId == session.ClassId && cs.StudentId == sid, ct))
                    throw new ConflictException($"Student {sid} is not enrolled in this session's class.");
                computer.StudentId = a.StudentId;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
