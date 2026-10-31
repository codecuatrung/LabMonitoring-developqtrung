namespace LabMonitoring.Domain.Common;

/// <summary>
/// The function screens seeded on migration (SRS FR-8 "màn hình chức năng"). Ids/Codes are fixed so
/// the `[Authorize(Policy = "screen:{Code}")]` attributes on controllers and the seeded
/// RoleScreenPermission rows stay stable across environments — mirrors <see cref="SeededRoles"/>.
/// </summary>
public static class SeededScreens
{
    public const int DashboardId = 1;
    public const int RoomsId = 2;
    public const int ClassesId = 3;
    public const int SessionsId = 4;
    public const int CatalogId = 5;
    public const int ReportsUsageId = 6;
    public const int UsersId = 7;
    public const int RolesId = 8;
    public const int ScreensId = 9;
    public const int AuditLogsId = 10;
    public const int SystemId = 11;
    public const int AgentUpdatesId = 12;
    public const int FileTransferId = 13;

    public const string Dashboard = "dashboard";
    public const string Rooms = "rooms";
    public const string Classes = "classes";
    public const string Sessions = "sessions";
    public const string Catalog = "catalog";
    public const string ReportsUsage = "reports.usage";
    public const string Users = "users";
    public const string Roles = "roles";
    public const string Screens = "screens";
    public const string AuditLogs = "audit-logs";
    public const string AgentUpdates = "agent-updates";
    public const string FileTransfer = "file-transfer";

    /// <summary>Sidebar submenu group ("Hệ thống") — Route is null, it's not a page of its own.
    /// Users/Roles/Screens nest under it via <c>FunctionScreen.ParentId</c>.</summary>
    public const string System = "system";
}
