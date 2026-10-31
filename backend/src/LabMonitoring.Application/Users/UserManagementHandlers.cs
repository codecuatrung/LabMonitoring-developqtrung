using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Users;

// Full user/role/room-scope management (SRS FR-8.1/8.2). There is deliberately no "create user"
// here: accounts are JIT-provisioned on first Keycloak sign-in (UserProvisioner, keyed on `sub`),
// so a pre-created row with no `sub` yet would never be matched and first login would create a
// duplicate. Management here only edits an already-provisioned account.

public sealed record UserDetailDto(
    int Id, string Username, string FullName, string? Email, string? Phone, bool IsActive,
    IReadOnlyList<int> RoleIds, IReadOnlyList<int> RoomScopeIds, IReadOnlyList<UserScreenOverrideDto> ScreenOverrides);

public sealed record UserScreenOverrideDto(int ScreenId, bool IsGranted);

// --- Queries ---

public sealed record GetUserQuery(int Id) : IRequest<UserDetailDto>;

internal sealed class GetUserHandler(IAppDbContext db) : IRequestHandler<GetUserQuery, UserDetailDto>
{
    public async Task<UserDetailDto> Handle(GetUserQuery request, CancellationToken ct)
        => await db.Users.Where(u => u.Id == request.Id)
            .Select(u => new UserDetailDto(
                u.Id, u.Username, u.FullName, u.Email, u.Phone, u.IsActive,
                u.UserRoles.Select(ur => ur.RoleId).ToList(),
                u.RoomScopes.Select(s => s.RoomId).ToList(),
                u.ScreenPermissions.Select(p => new UserScreenOverrideDto(p.ScreenId, p.IsGranted)).ToList()))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), request.Id);
}

// --- Commands ---

public sealed record UpdateUserCommand(int Id, string FullName, string? Phone, bool IsActive) : IRequest;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}

internal sealed class UpdateUserHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(User), request.Id);

        if (!request.IsActive && user.IsActive)
            await LastAdministratorGuard.GuardAsync(db, user, ct);

        user.FullName = request.FullName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        user.IsActive = request.IsActive;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "User.Update",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}

public sealed record SetUserRolesCommand(int Id, IReadOnlyList<int> RoleIds) : IRequest;

internal sealed class SetUserRolesHandler(IAppDbContext db, ICurrentUser currentUser, IScreenAccessService screenAccess)
    : IRequestHandler<SetUserRolesCommand>
{
    public async Task Handle(SetUserRolesCommand request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(User), request.Id);

        var validRoleIds = await db.Roles.Select(r => r.Id).ToListAsync(ct);
        var newRoleIds = request.RoleIds.Distinct().Where(validRoleIds.Contains).ToHashSet();

        if (user.UserRoles.Any(ur => ur.RoleId == SeededRoles.AdministratorId) && !newRoleIds.Contains(SeededRoles.AdministratorId))
            await LastAdministratorGuard.GuardAsync(db, user, ct);

        db.UserRoles.RemoveRange(user.UserRoles.Where(ur => !newRoleIds.Contains(ur.RoleId)));
        foreach (var roleId in newRoleIds.Where(id => user.UserRoles.All(ur => ur.RoleId != id)))
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "User.SetRoles",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            Detail = string.Join(',', newRoleIds),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        screenAccess.InvalidateCache();
    }
}

public sealed record SetUserRoomScopesCommand(int Id, IReadOnlyList<int> RoomIds) : IRequest;

internal sealed class SetUserRoomScopesHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<SetUserRoomScopesCommand>
{
    public async Task Handle(SetUserRoomScopesCommand request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.RoomScopes).FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(User), request.Id);

        var validRoomIds = await db.Rooms.Select(r => r.Id).ToListAsync(ct);
        var newRoomIds = request.RoomIds.Distinct().Where(validRoomIds.Contains).ToHashSet();

        db.UserRoomScopes.RemoveRange(user.RoomScopes.Where(s => !newRoomIds.Contains(s.RoomId)));
        foreach (var roomId in newRoomIds.Where(id => user.RoomScopes.All(s => s.RoomId != id)))
            db.UserRoomScopes.Add(new UserRoomScope { UserId = user.Id, RoomId = roomId });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "User.SetRoomScopes",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            Detail = string.Join(',', newRoomIds),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}

public sealed record SetUserScreenOverridesCommand(int Id, IReadOnlyList<UserScreenOverrideDto> Overrides) : IRequest;

/// <summary>Per-user screen access override, independent of role (SRS FR-8 "user độc lập") — a
/// screen in <paramref name="request"/>.Overrides not present at all means "no override, fall back
/// to role", matching UserScreenPermission's own row-presence semantics.</summary>
internal sealed class SetUserScreenOverridesHandler(IAppDbContext db, ICurrentUser currentUser, IScreenAccessService screenAccess)
    : IRequestHandler<SetUserScreenOverridesCommand>
{
    public async Task Handle(SetUserScreenOverridesCommand request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.ScreenPermissions).FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(User), request.Id);

        var validScreenIds = await db.FunctionScreens.Select(s => s.Id).ToListAsync(ct);
        var newOverrides = request.Overrides
            .Where(o => validScreenIds.Contains(o.ScreenId))
            .DistinctBy(o => o.ScreenId)
            .ToDictionary(o => o.ScreenId, o => o.IsGranted);

        db.UserScreenPermissions.RemoveRange(user.ScreenPermissions.Where(p => !newOverrides.ContainsKey(p.ScreenId)));
        foreach (var existing in user.ScreenPermissions.Where(p => newOverrides.ContainsKey(p.ScreenId)))
            existing.IsGranted = newOverrides[existing.ScreenId];
        foreach (var (screenId, isGranted) in newOverrides.Where(o => user.ScreenPermissions.All(p => p.ScreenId != o.Key)))
            db.UserScreenPermissions.Add(new UserScreenPermission { UserId = user.Id, ScreenId = screenId, IsGranted = isGranted });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "User.SetScreenOverrides",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            Detail = string.Join(',', newOverrides.Select(o => $"{o.Key}:{(o.Value ? "grant" : "revoke")}")),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        screenAccess.InvalidateCache();
    }
}

/// <summary>Refuses to deactivate/demote the sole remaining active Administrator, to avoid locking
/// every admin console out of the system (no other way back in without direct DB access).</summary>
internal static class LastAdministratorGuard
{
    public static async Task GuardAsync(IAppDbContext db, User user, CancellationToken ct)
    {
        var isCurrentlyAdmin = await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == SeededRoles.AdministratorId, ct);
        if (!isCurrentlyAdmin)
            return;

        var otherActiveAdmins = await db.Users
            .Where(u => u.Id != user.Id && u.IsActive && u.UserRoles.Any(ur => ur.RoleId == SeededRoles.AdministratorId))
            .AnyAsync(ct);

        if (!otherActiveAdmins)
            throw new ConflictException("Cannot deactivate or demote the last remaining Administrator.");
    }
}
