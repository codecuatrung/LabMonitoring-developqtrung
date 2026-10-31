namespace LabMonitoring.Domain.Identity;

/// <summary>Role. Seeded: Administrator, Technician, Teacher (SRS FR-8.1) — those three carry
/// hard-coded business behavior (bootstrap admin, last-administrator guard, class/session ownership,
/// room scope) and are marked <see cref="IsSystem"/> so they can't be renamed or deleted. Additional
/// roles can be created freely; they only ever affect screen access (RoleScreenPermission), never
/// the seeded roles' special behavior.</summary>
public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsSystem { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<RoleScreenPermission> ScreenPermissions { get; set; } = [];
}
