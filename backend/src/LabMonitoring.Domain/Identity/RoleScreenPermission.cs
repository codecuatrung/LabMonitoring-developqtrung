namespace LabMonitoring.Domain.Identity;

/// <summary>Many-to-many Role ↔ FunctionScreen (composite PK). Row presence = the role can access
/// the screen; there is no separate "denied" state at the role level (see UserScreenPermission for
/// the per-user override, which does have one).</summary>
public class RoleScreenPermission
{
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public int ScreenId { get; set; }
    public FunctionScreen Screen { get; set; } = null!;
}
