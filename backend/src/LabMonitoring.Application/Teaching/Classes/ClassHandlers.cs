using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Domain.Identity;
using LabMonitoring.Domain.Teaching;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Teaching.Classes;

// --- Queries ---

public sealed record GetClassesQuery(int? TeacherId = null, int Page = 1, int PageSize = 20) : IRequest<PagedResult<ClassDto>>;

internal sealed class GetClassesHandler(IAppDbContext db) : IRequestHandler<GetClassesQuery, PagedResult<ClassDto>>
{
    public async Task<PagedResult<ClassDto>> Handle(GetClassesQuery request, CancellationToken ct)
    {
        var query = db.Classes.AsQueryable();
        if (request.TeacherId is { } teacherId)
            query = query.Where(c => c.TeacherId == teacherId);

        query = query.OrderBy(c => c.Code);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClassDto(c.Id, c.Code, c.Name, c.Subject, c.TeacherId, c.Teacher.FullName,
                c.Semester, c.ClassStudents.Count))
            .ToListAsync(ct);

        return new PagedResult<ClassDto>(items, total, page, pageSize);
    }
}

public sealed record GetClassQuery(int Id) : IRequest<ClassDetailDto>;

internal sealed class GetClassHandler(IAppDbContext db) : IRequestHandler<GetClassQuery, ClassDetailDto>
{
    public async Task<ClassDetailDto> Handle(GetClassQuery request, CancellationToken ct) =>
        await db.Classes
            .Where(c => c.Id == request.Id)
            .Select(c => new ClassDetailDto(c.Id, c.Code, c.Name, c.Subject, c.TeacherId, c.Teacher.FullName, c.Semester,
                c.ClassStudents.OrderBy(cs => cs.Student.StudentCode)
                    .Select(cs => new StudentDto(cs.Student.Id, cs.Student.StudentCode, cs.Student.FullName, cs.Student.Email))
                    .ToList()))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Class), request.Id);
}

// --- Commands ---

public sealed record CreateClassCommand(string Code, string Name, string Subject, int TeacherId, string? Semester)
    : IRequest<int>;

public sealed class CreateClassValidator : AbstractValidator<CreateClassCommand>
{
    public CreateClassValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Semester).MaximumLength(20);
    }
}

internal sealed class CreateClassHandler(IAppDbContext db) : IRequestHandler<CreateClassCommand, int>
{
    public async Task<int> Handle(CreateClassCommand request, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == request.TeacherId, ct))
            throw new NotFoundException(nameof(User), request.TeacherId);
        if (await db.Classes.AnyAsync(c => c.Code == request.Code, ct))
            throw new ConflictException($"Class code '{request.Code}' is already in use.");

        var entity = new Class
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Subject = request.Subject.Trim(),
            TeacherId = request.TeacherId,
            Semester = string.IsNullOrWhiteSpace(request.Semester) ? null : request.Semester.Trim(),
        };
        db.Classes.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }
}

public sealed record UpdateClassCommand(int Id, string Code, string Name, string Subject, int TeacherId, string? Semester)
    : IRequest;

public sealed class UpdateClassValidator : AbstractValidator<UpdateClassCommand>
{
    public UpdateClassValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(150);
    }
}

internal sealed class UpdateClassHandler(IAppDbContext db) : IRequestHandler<UpdateClassCommand>
{
    public async Task Handle(UpdateClassCommand request, CancellationToken ct)
    {
        var entity = await db.Classes.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Class), request.Id);
        if (!await db.Users.AnyAsync(u => u.Id == request.TeacherId, ct))
            throw new NotFoundException(nameof(User), request.TeacherId);
        if (await db.Classes.AnyAsync(c => c.Code == request.Code && c.Id != request.Id, ct))
            throw new ConflictException($"Class code '{request.Code}' is already in use.");

        entity.Code = request.Code.Trim();
        entity.Name = request.Name.Trim();
        entity.Subject = request.Subject.Trim();
        entity.TeacherId = request.TeacherId;
        entity.Semester = string.IsNullOrWhiteSpace(request.Semester) ? null : request.Semester.Trim();
        await db.SaveChangesAsync(ct);
    }
}

public sealed record DeleteClassCommand(int Id) : IRequest;

internal sealed class DeleteClassHandler(IAppDbContext db) : IRequestHandler<DeleteClassCommand>
{
    public async Task Handle(DeleteClassCommand request, CancellationToken ct)
    {
        var entity = await db.Classes.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Class), request.Id);

        if (await db.Sessions.AnyAsync(s => s.ClassId == request.Id, ct))
            throw new ConflictException("This class already has sessions recorded; it cannot be deleted.");

        db.Classes.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}

// --- Enrollment ---

public sealed record EnrollStudentCommand(int ClassId, int StudentId) : IRequest;

internal sealed class EnrollStudentHandler(IAppDbContext db) : IRequestHandler<EnrollStudentCommand>
{
    public async Task Handle(EnrollStudentCommand request, CancellationToken ct)
    {
        if (!await db.Classes.AnyAsync(c => c.Id == request.ClassId, ct))
            throw new NotFoundException(nameof(Class), request.ClassId);
        if (!await db.Students.AnyAsync(s => s.Id == request.StudentId, ct))
            throw new NotFoundException(nameof(Student), request.StudentId);
        if (await db.ClassStudents.AnyAsync(cs => cs.ClassId == request.ClassId && cs.StudentId == request.StudentId, ct))
            throw new ConflictException("This student is already enrolled in the class.");

        db.ClassStudents.Add(new ClassStudent { ClassId = request.ClassId, StudentId = request.StudentId });
        await db.SaveChangesAsync(ct);
    }
}

public sealed record UnenrollStudentCommand(int ClassId, int StudentId) : IRequest;

internal sealed class UnenrollStudentHandler(IAppDbContext db) : IRequestHandler<UnenrollStudentCommand>
{
    public async Task Handle(UnenrollStudentCommand request, CancellationToken ct)
    {
        var link = await db.ClassStudents
            .FirstOrDefaultAsync(cs => cs.ClassId == request.ClassId && cs.StudentId == request.StudentId, ct)
            ?? throw new NotFoundException("ClassStudent", $"{request.ClassId}/{request.StudentId}");

        db.ClassStudents.Remove(link);
        await db.SaveChangesAsync(ct);
    }
}
