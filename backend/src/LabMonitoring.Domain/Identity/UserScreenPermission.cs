namespace LabMonitoring.Domain.Identity;

/// <summary>
/// Per-user override of screen access, independent of role (composite PK). Row presence means an
/// override exists: <see cref="IsGranted"/> true grants the screen even if no role does, false
/// revokes it even if a role would otherwise grant it. No row = fall back to the role default.
/// </summary>
public class UserScreenPermission
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int ScreenId { get; set; }
    public FunctionScreen Screen { get; set; } = null!;

    public bool IsGranted { get; set; }
}
