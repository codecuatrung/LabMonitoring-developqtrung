namespace LabMonitoring.Domain.Identity;

/// <summary>
/// A registered UI screen/menu item (SRS FR-8 "màn hình chức năng"). Every top-level admin-console
/// page must have a row here — access is granted via <see cref="RoleScreenPermission"/> and
/// individually overridden via <see cref="UserScreenPermission"/>. A screen with <see cref="Route"/>
/// null and children present is a pure sidebar submenu grouping (e.g. "Hệ thống"), not a page of its
/// own — it still goes through the same permission system as any other screen.
/// </summary>
public class FunctionScreen
{
    public int Id { get; set; }

    /// <summary>Stable key used in code — the ASP.NET Core policy name is <c>"screen:{Code}"</c>.</summary>
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Null for a group-only node (submenu header with no page of its own).</summary>
    public string? Route { get; set; }

    public string? Icon { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>Null for a top-level sidebar entry; set to nest under a submenu group.</summary>
    public int? ParentId { get; set; }
    public FunctionScreen? Parent { get; set; }
    public ICollection<FunctionScreen> Children { get; set; } = [];

    public ICollection<RoleScreenPermission> RolePermissions { get; set; } = [];
    public ICollection<UserScreenPermission> UserPermissions { get; set; } = [];
}
