namespace LabMonitoring.Domain.Identity;

/// <summary>
/// Admin-side account (Administrator / Technician / Teacher). Identity is provided by the shared
/// Keycloak realm; <see cref="KeycloakUserId"/> is the token <c>sub</c>. Students are not Users.
/// </summary>
public class User
{
    public int Id { get; set; }

    /// <summary>Keycloak <c>sub</c>. Null until the account signs in for the first time.</summary>
    public Guid? KeycloakUserId { get; set; }

    public string Username { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<UserRoomScope> RoomScopes { get; set; } = [];
    public ICollection<UserScreenPermission> ScreenPermissions { get; set; } = [];
}
