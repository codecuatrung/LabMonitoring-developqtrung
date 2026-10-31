using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Teaching;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Teaching.Students;

// --- Queries ---

public sealed record GetStudentsQuery(string? Search = null) : IRequest<IReadOnlyList<StudentDto>>;

internal sealed class GetStudentsHandler(IAppDbContext db) : IRequestHandler<GetStudentsQuery, IReadOnlyList<StudentDto>>
{
    public async Task<IReadOnlyList<StudentDto>> Handle(GetStudentsQuery request, CancellationToken ct)
    {
        var query = db.Students.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(s => s.StudentCode.Contains(term) || s.FullName.Contains(term));
        }

        return await query
            .OrderBy(s => s.StudentCode)
            .Select(s => new StudentDto(s.Id, s.StudentCode, s.FullName, s.Email))
            .ToListAsync(ct);
    }
}

// --- Commands ---

public sealed record CreateStudentCommand(string StudentCode, string FullName, string? Email) : IRequest<int>;

public sealed class CreateStudentValidator : AbstractValidator<CreateStudentCommand>
{
    public CreateStudentValidator()
    {
        RuleFor(x => x.StudentCode).NotEmpty().MaximumLength(30);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

internal sealed class CreateStudentHandler(IAppDbContext db) : IRequestHandler<CreateStudentCommand, int>
{
    public async Task<int> Handle(CreateStudentCommand request, CancellationToken ct)
    {
        if (await db.Students.AnyAsync(s => s.StudentCode == request.StudentCode, ct))
            throw new ConflictException($"Student code '{request.StudentCode}' is already in use.");

        var entity = new Student
        {
            StudentCode = request.StudentCode.Trim(),
            FullName = request.FullName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
        };
        db.Students.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }
}

public sealed record UpdateStudentCommand(int Id, string StudentCode, string FullName, string? Email) : IRequest;

public sealed class UpdateStudentValidator : AbstractValidator<UpdateStudentCommand>
{
    public UpdateStudentValidator()
    {
        RuleFor(x => x.StudentCode).NotEmpty().MaximumLength(30);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

internal sealed class UpdateStudentHandler(IAppDbContext db) : IRequestHandler<UpdateStudentCommand>
{
    public async Task Handle(UpdateStudentCommand request, CancellationToken ct)
    {
        var entity = await db.Students.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Student), request.Id);
        if (await db.Students.AnyAsync(s => s.StudentCode == request.StudentCode && s.Id != request.Id, ct))
            throw new ConflictException($"Student code '{request.StudentCode}' is already in use.");

        entity.StudentCode = request.StudentCode.Trim();
        entity.FullName = request.FullName.Trim();
        entity.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        await db.SaveChangesAsync(ct);
    }
}

public sealed record DeleteStudentCommand(int Id) : IRequest;

internal sealed class DeleteStudentHandler(IAppDbContext db) : IRequestHandler<DeleteStudentCommand>
{
    public async Task Handle(DeleteStudentCommand request, CancellationToken ct)
    {
        var entity = await db.Students.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Student), request.Id);

        db.Students.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
