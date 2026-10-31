using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Commands;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Identity;
using LabMonitoring.Domain.Policies;
using LabMonitoring.Domain.Screenshots;
using LabMonitoring.Domain.Sessions;
using LabMonitoring.Domain.Teaching;
using LabMonitoring.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LabMonitoring.Infrastructure.Persistence;

public class LabMonitoringDbContext(DbContextOptions<LabMonitoringDbContext> options) : DbContext(options), IAppDbContext
{
    // A. Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserRoomScope> UserRoomScopes => Set<UserRoomScope>();
    public DbSet<FunctionScreen> FunctionScreens => Set<FunctionScreen>();
    public DbSet<RoleScreenPermission> RoleScreenPermissions => Set<RoleScreenPermission>();
    public DbSet<UserScreenPermission> UserScreenPermissions => Set<UserScreenPermission>();

    // B. Facilities
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Computer> Computers => Set<Computer>();

    // C. Telemetry
    public DbSet<ComputerSpec> ComputerSpecs => Set<ComputerSpec>();
    public DbSet<ComputerStatus> ComputerStatuses => Set<ComputerStatus>();
    public DbSet<ComputerMetric> ComputerMetrics => Set<ComputerMetric>();
    public DbSet<AgentPackage> AgentPackages => Set<AgentPackage>();
    public DbSet<TransferFolder> TransferFolders => Set<TransferFolder>();
    public DbSet<FileTransfer> FileTransfers => Set<FileTransfer>();
    public DbSet<CollectedFile> CollectedFiles => Set<CollectedFile>();

    // D. Teaching
    public DbSet<Class> Classes => Set<Class>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<ClassStudent> ClassStudents => Set<ClassStudent>();
    public DbSet<Schedule> Schedules => Set<Schedule>();

    // E. Sessions
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionComputer> SessionComputers => Set<SessionComputer>();

    // F. Commands
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<CommandResult> CommandResults => Set<CommandResult>();

    // G. Policies
    public DbSet<ManagedApplication> Applications => Set<ManagedApplication>();
    public DbSet<ApplicationPolicy> ApplicationPolicies => Set<ApplicationPolicy>();
    public DbSet<WebsitePolicy> WebsitePolicies => Set<WebsitePolicy>();

    // H. Screenshots
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();

    // I. Auditing
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    // SQL Server's datetime2 columns carry no timezone info, so EF Core always reads DateTime values
    // back with Kind=Unspecified — even though every write in this app uses DateTime.UtcNow. System.
    // Text.Json then serializes those WITHOUT a trailing "Z", so the browser's Date parser (and
    // Angular's DatePipe) treats the raw UTC digits as if they were already local time, showing
    // every timestamp in the UI exactly UtcOffset hours off (e.g. 7h behind in Asia/Ho_Chi_Minh).
    // Stamping Kind=Utc back on the way out fixes serialization for every entity at once, with no
    // per-DTO/per-handler changes needed.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LabMonitoringDbContext).Assembly);

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = Domain.Common.SeededRoles.AdministratorId, Name = Domain.Common.SeededRoles.Administrator, IsSystem = true },
            new Role { Id = Domain.Common.SeededRoles.TechnicianId, Name = Domain.Common.SeededRoles.Technician, IsSystem = true },
            new Role { Id = Domain.Common.SeededRoles.TeacherId, Name = Domain.Common.SeededRoles.Teacher, IsSystem = true });

        SeedFunctionScreens(modelBuilder);
        SeedRoleScreenPermissions(modelBuilder);
    }

    /// <summary>The function screens shipped with the app (SRS FR-8). New screens an admin
    /// registers via the API land as ordinary rows, not here — this is only the initial set.</summary>
    private static void SeedFunctionScreens(ModelBuilder modelBuilder)
    {
        var seededAt = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<FunctionScreen>().HasData(
            new FunctionScreen { Id = Domain.Common.SeededScreens.DashboardId, Code = Domain.Common.SeededScreens.Dashboard, Name = "Bảng điều khiển", Route = "/dashboard", Icon = "bi-speedometer2", DisplayOrder = 1, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.RoomsId, Code = Domain.Common.SeededScreens.Rooms, Name = "Phòng máy", Route = "/rooms", Icon = "bi-hdd-network", DisplayOrder = 2, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.ClassesId, Code = Domain.Common.SeededScreens.Classes, Name = "Lớp học", Route = "/classes", Icon = "bi-mortarboard", DisplayOrder = 3, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.SessionsId, Code = Domain.Common.SeededScreens.Sessions, Name = "Ca thực hành", Route = "/sessions", Icon = "bi-calendar-week", DisplayOrder = 4, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.CatalogId, Code = Domain.Common.SeededScreens.Catalog, Name = "Danh mục", Route = "/catalog", Icon = "bi-gear", DisplayOrder = 5, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.ReportsUsageId, Code = Domain.Common.SeededScreens.ReportsUsage, Name = "Báo cáo", Route = "/reports", Icon = "bi-bar-chart", DisplayOrder = 6, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.SystemId, Code = Domain.Common.SeededScreens.System, Name = "Hệ thống", Route = null, Icon = "bi-diagram-3", DisplayOrder = 7, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.UsersId, Code = Domain.Common.SeededScreens.Users, Name = "Người dùng", Route = "/users", Icon = "bi-people", DisplayOrder = 1, ParentId = Domain.Common.SeededScreens.SystemId, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.RolesId, Code = Domain.Common.SeededScreens.Roles, Name = "Vai trò", Route = "/roles", Icon = "bi-person-badge", DisplayOrder = 2, ParentId = Domain.Common.SeededScreens.SystemId, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.ScreensId, Code = Domain.Common.SeededScreens.Screens, Name = "Màn hình chức năng", Route = "/screens", Icon = "bi-window", DisplayOrder = 3, ParentId = Domain.Common.SeededScreens.SystemId, CreatedAt = seededAt },
            new FunctionScreen { Id = Domain.Common.SeededScreens.AuditLogsId, Code = Domain.Common.SeededScreens.AuditLogs, Name = "Nhật ký", Route = "/audit-logs", Icon = "bi-journal-text", DisplayOrder = 8, CreatedAt = seededAt });
    }

    /// <summary>
    /// Matches the access every role already has today via the old static policies exactly, so
    /// switching controllers over to screen-based authorization doesn't silently change anyone's
    /// access: Technician/Teacher could already reach every AdminConsole-gated page (Dashboard/
    /// Rooms/Classes/Sessions/Reports) but NOT Catalog — <c>BuildingsController</c>, which the
    /// Catalog page depends on for building CRUD, has always been Administrator-only, stricter than
    /// AdminConsole, unlike every other screen here. Users/Audit Logs/Roles/Screens are
    /// Administrator-only too (Users/Audit Logs already were; Roles/Screens are new pages).
    /// </summary>
    private static void SeedRoleScreenPermissions(ModelBuilder modelBuilder)
    {
        var allScreenIds = new[]
        {
            Domain.Common.SeededScreens.DashboardId, Domain.Common.SeededScreens.RoomsId, Domain.Common.SeededScreens.ClassesId,
            Domain.Common.SeededScreens.SessionsId, Domain.Common.SeededScreens.CatalogId, Domain.Common.SeededScreens.ReportsUsageId,
            Domain.Common.SeededScreens.UsersId, Domain.Common.SeededScreens.RolesId, Domain.Common.SeededScreens.ScreensId,
            Domain.Common.SeededScreens.AuditLogsId, Domain.Common.SeededScreens.SystemId,
        };
        var staffScreenIds = new[]
        {
            Domain.Common.SeededScreens.DashboardId, Domain.Common.SeededScreens.RoomsId, Domain.Common.SeededScreens.ClassesId,
            Domain.Common.SeededScreens.SessionsId, Domain.Common.SeededScreens.ReportsUsageId,
        };

        var rows = new List<RoleScreenPermission>();
        rows.AddRange(allScreenIds.Select(screenId => new RoleScreenPermission { RoleId = Domain.Common.SeededRoles.AdministratorId, ScreenId = screenId }));
        rows.AddRange(staffScreenIds.Select(screenId => new RoleScreenPermission { RoleId = Domain.Common.SeededRoles.TechnicianId, ScreenId = screenId }));
        rows.AddRange(staffScreenIds.Select(screenId => new RoleScreenPermission { RoleId = Domain.Common.SeededRoles.TeacherId, ScreenId = screenId }));

        modelBuilder.Entity<RoleScreenPermission>().HasData(rows);
    }
}
