namespace LabMonitoring.Domain.Identity;

/// <summary>Many-to-many User ↔ Role (composite PK).</summary>
public class UserRole
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
