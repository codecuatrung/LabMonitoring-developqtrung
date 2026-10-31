using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Screens;

public sealed record ScreenDto(
    int Id, string Code, string Name, string? Route, string? Icon, int DisplayOrder, bool IsActive, int? ParentId);

// --- Queries ---

/// <summary>Full registry, for the "Màn hình chức năng" admin page (SRS FR-8).</summary>
public sealed record GetScreensQuery : IRequest<IReadOnlyList<ScreenDto>>;

internal sealed class GetScreensHandler(IAppDbContext db) : IRequestHandler<GetScreensQuery, IReadOnlyList<ScreenDto>>
{
    public async Task<IReadOnlyList<ScreenDto>> Handle(GetScreensQuery request, CancellationToken ct)
        => await db.FunctionScreens
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new ScreenDto(s.Id, s.Code, s.Name, s.Route, s.Icon, s.DisplayOrder, s.IsActive, s.ParentId))
            .ToListAsync(ct);
}

/// <summary>Only the screens the caller can actually access — drives the Angular menu + route guard.
/// Includes group-only nodes (e.g. "Hệ thống") the caller has access to, even though they have no
/// route of their own, so the frontend can render the submenu header; a group with none of its
/// children accessible simply won't appear (nothing to nest under it).</summary>
public sealed record GetMyScreensQuery : IRequest<IReadOnlyList<ScreenDto>>;

internal sealed class GetMyScreensHandler(IAppDbContext db, ICurrentUser currentUser, IScreenAccessService screenAccess)
    : IRequestHandler<GetMyScreensQuery, IReadOnlyList<ScreenDto>>
{
    public async Task<IReadOnlyList<ScreenDto>> Handle(GetMyScreensQuery request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException();
        var accessible = await screenAccess.GetAccessibleScreenCodesAsync(userId, currentUser.IsAdministrator, ct);

        return await db.FunctionScreens
            .Where(s => s.IsActive && accessible.Contains(s.Code))
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new ScreenDto(s.Id, s.Code, s.Name, s.Route, s.Icon, s.DisplayOrder, s.IsActive, s.ParentId))
            .ToListAsync(ct);
    }
}

// --- Commands ---

public sealed record CreateScreenCommand(string Code, string Name, string? Route, string? Icon, int DisplayOrder, int? ParentId) : IRequest<int>;

public sealed class CreateScreenValidator : AbstractValidator<CreateScreenCommand>
{
    public CreateScreenValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100).Matches("^[a-z0-9.-]+$")
            .WithMessage("Code chỉ gồm chữ thường, số, dấu chấm và gạch ngang.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Route).MaximumLength(200);
        RuleFor(x => x.Icon).MaximumLength(50);
    }
}

internal sealed class CreateScreenHandler(IAppDbContext db, ICurrentUser currentUser, IScreenAccessService screenAccess)
    : IRequestHandler<CreateScreenCommand, int>
{
    public async Task<int> Handle(CreateScreenCommand request, CancellationToken ct)
    {
        if (await db.FunctionScreens.AnyAsync(s => s.Code == request.Code, ct))
            throw new ConflictException($"Screen code '{request.Code}' is already in use.");
        if (request.ParentId is { } parentId && !await db.FunctionScreens.AnyAsync(s => s.Id == parentId, ct))
            throw new NotFoundException(nameof(FunctionScreen), parentId);

        var screen = new FunctionScreen
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Route = string.IsNullOrWhiteSpace(request.Route) ? null : request.Route.Trim(),
            Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim(),
            DisplayOrder = request.DisplayOrder,
            ParentId = request.ParentId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.FunctionScreens.Add(screen);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Screen.Create",
            EntityType = nameof(FunctionScreen),
            Detail = request.Code,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        screenAccess.InvalidateCache();
        return screen.Id;
    }
}

public sealed record UpdateScreenCommand(int Id, string Name, string? Route, string? Icon, int DisplayOrder, bool IsActive, int? ParentId) : IRequest;

public sealed class UpdateScreenValidator : AbstractValidator<UpdateScreenCommand>
{
    public UpdateScreenValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Route).MaximumLength(200);
        RuleFor(x => x.Icon).MaximumLength(50);
    }
}

internal sealed class UpdateScreenHandler(IAppDbContext db, ICurrentUser currentUser, IScreenAccessService screenAccess)
    : IRequestHandler<UpdateScreenCommand>
{
    public async Task Handle(UpdateScreenCommand request, CancellationToken ct)
    {
        var screen = await db.FunctionScreens.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(FunctionScreen), request.Id);

        if (request.ParentId == request.Id)
            throw new ConflictException("A screen cannot be its own parent.");
        if (request.ParentId is { } parentId && !await db.FunctionScreens.AnyAsync(s => s.Id == parentId, ct))
            throw new NotFoundException(nameof(FunctionScreen), parentId);

        screen.Name = request.Name.Trim();
        screen.Route = string.IsNullOrWhiteSpace(request.Route) ? null : request.Route.Trim();
        screen.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
        screen.DisplayOrder = request.DisplayOrder;
        screen.IsActive = request.IsActive;
        screen.ParentId = request.ParentId;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Screen.Update",
            EntityType = nameof(FunctionScreen),
            EntityId = screen.Id.ToString(),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        screenAccess.InvalidateCache();
    }
}

public sealed record DeleteScreenCommand(int Id) : IRequest;

internal sealed class DeleteScreenHandler(IAppDbContext db, ICurrentUser currentUser, IScreenAccessService screenAccess)
    : IRequestHandler<DeleteScreenCommand>
{
    public async Task Handle(DeleteScreenCommand request, CancellationToken ct)
    {
        var screen = await db.FunctionScreens.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(FunctionScreen), request.Id);

        if (await db.FunctionScreens.AnyAsync(s => s.ParentId == request.Id, ct))
            throw new ConflictException("Cannot delete a screen that has child screens nested under it.");

        db.FunctionScreens.Remove(screen);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Screen.Delete",
            EntityType = nameof(FunctionScreen),
            EntityId = screen.Id.ToString(),
            Detail = screen.Code,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        screenAccess.InvalidateCache();
    }
}
