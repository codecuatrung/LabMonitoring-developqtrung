using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Notifications;

public sealed record NotificationDto(long Id, string Title, string Message, string Severity, bool IsRead, DateTime CreatedAt);

// --- Queries ---

/// <summary>The caller's own in-app notifications (SRS FR-7.2) — never another user's.</summary>
public sealed record GetMyNotificationsQuery(bool UnreadOnly = false, int Limit = 50) : IRequest<IReadOnlyList<NotificationDto>>;

internal sealed class GetMyNotificationsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyNotificationsQuery, IReadOnlyList<NotificationDto>>
{
    public async Task<IReadOnlyList<NotificationDto>> Handle(GetMyNotificationsQuery request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException();

        var query = db.Notifications.Where(n => n.UserId == userId);
        if (request.UnreadOnly)
            query = query.Where(n => !n.IsRead);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(request.Limit, 1, 200))
            .Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.Severity.ToString(), n.IsRead, n.CreatedAt))
            .ToListAsync(ct);
    }
}

public sealed record GetUnreadNotificationCountQuery : IRequest<int>;

internal sealed class GetUnreadNotificationCountHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetUnreadNotificationCountQuery, int>
{
    public Task<int> Handle(GetUnreadNotificationCountQuery request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException();
        return db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }
}

// --- Commands ---

public sealed record MarkNotificationReadCommand(long Id) : IRequest;

internal sealed class MarkNotificationReadHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<MarkNotificationReadCommand>
{
    public async Task Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException();
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == request.Id, ct)
            ?? throw new NotFoundException("Notification", request.Id);

        if (notification.UserId != userId)
            throw new ForbiddenException();

        notification.IsRead = true;
        await db.SaveChangesAsync(ct);
    }
}

public sealed record MarkAllNotificationsReadCommand : IRequest;

internal sealed class MarkAllNotificationsReadHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<MarkAllNotificationsReadCommand>
{
    public async Task Handle(MarkAllNotificationsReadCommand request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException();
        await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.IsRead, true), ct);
    }
}
