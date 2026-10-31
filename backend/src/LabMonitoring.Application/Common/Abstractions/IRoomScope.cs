namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// Resolves which rooms the current user may see and control (SRS FR-8.2). Administrators are
/// unrestricted; Technician / Teacher are limited to their assigned rooms (<c>UserRoomScopes</c>).
/// </summary>
public interface IRoomScope
{
    /// <summary>Room ids the user is limited to, or <c>null</c> when unrestricted (Administrator).</summary>
    Task<IReadOnlySet<int>?> GetAllowedRoomIdsAsync(CancellationToken ct = default);

    Task<bool> CanAccessRoomAsync(int roomId, CancellationToken ct = default);
}
