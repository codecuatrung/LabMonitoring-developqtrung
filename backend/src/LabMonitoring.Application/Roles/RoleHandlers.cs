using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Roles;

// Role CRUD (SRS FR-8.1) is deliberately open-ended — an admin can create any number of custom
// roles beyond the 3 seeded ones (Administrator/Technician/Teacher). Those three carry hard-coded
// business behavior elsewhere in the app (bootstrap admin, LastAdministratorGuard, class/session
// ownership, room scope) and are marked IsSystem so they can't be renamed or deleted here — a
// custom role only ever matters through RoleScreenPermission, nothing else.

public sealed record RoleDto(int Id, string Name, bool IsSystem, IReadOnlyList<int> ScreenIds);

// --- Queries ---

public sealed record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

internal sealed class GetRolesHandler(IAppDbContext db) : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct)
        => await db.Roles
            .OrderBy(r => r.Id)
            .Select(r => new RoleDto(r.Id, r.Name, r.IsSystem, r.ScreenPermissions.Select(p => p.ScreenId).ToList()))
            .ToListAsync(ct);
}

// --- Commands ---

public sealed record CreateRoleCommand(string Name) : IRequest<int>;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
}

internal sealed class CreateRoleHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<CreateRoleCommand, int>
{
    public async Task<int> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name, ct))
            throw new ConflictException($"Role '{name}' already exists.");

        var role = new Role { Name = name, IsSystem = false };
        db.Roles.Add(role);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Role.Create",
            EntityType = nameof(Role),
            Detail = name,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return role.Id;
    }
}

public sealed record UpdateRoleCommand(int Id, string Name) : IRequest;

public sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
}

internal sealed class UpdateRoleHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<UpdateRoleCommand>
{
    public async Task Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Role), request.Id);

        if (role.IsSystem)
            throw new ConflictException("System roles cannot be renamed.");

        var name = request.Name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != request.Id, ct))
            throw new ConflictException($"Role '{name}' already exists.");

        role.Name = name;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Role.Update",
            EntityType = nameof(Role),
            EntityId = role.Id.ToString(),
            Detail = name,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}

public sealed record DeleteRoleCommand(int Id) : IRequest;

internal sealed class DeleteRoleHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Role), request.Id);

        if (role.IsSystem)
            throw new ConflictException("System roles cannot be deleted.");
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == request.Id, ct))
            throw new ConflictException("Cannot delete a role that is still assigned to users.");

        db.Roles.Remove(role);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Role.Delete",
            EntityType = nameof(Role),
            EntityId = role.Id.ToString(),
            Detail = role.Name,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}

public sealed record SetRoleScreensCommand(int Id, IReadOnlyList<int> ScreenIds) : IRequest;

internal sealed class SetRoleScreensHandler(IAppDbContext db, ICurrentUser currentUser, IScreenAccessService screenAccess)
    : IRequestHandler<SetRoleScreensCommand>
{
    public async Task Handle(SetRoleScreensCommand request, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.ScreenPermissions).FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Role), request.Id);

        var validScreenIds = await db.FunctionScreens.Select(s => s.Id).ToListAsync(ct);
        var newScreenIds = request.ScreenIds.Distinct().Where(validScreenIds.Contains).ToHashSet();

        db.RoleScreenPermissions.RemoveRange(role.ScreenPermissions.Where(p => !newScreenIds.Contains(p.ScreenId)));
        foreach (var screenId in newScreenIds.Where(id => role.ScreenPermissions.All(p => p.ScreenId != id)))
            db.RoleScreenPermissions.Add(new RoleScreenPermission { RoleId = role.Id, ScreenId = screenId });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Role.SetScreens",
            EntityType = nameof(Role),
            EntityId = role.Id.ToString(),
            Detail = string.Join(',', newScreenIds),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        screenAccess.InvalidateCache();
    }
}
