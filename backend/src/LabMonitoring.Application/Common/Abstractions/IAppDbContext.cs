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

namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// The persistence surface the Application layer uses, so handlers depend on an abstraction rather
/// than the concrete <c>LabMonitoringDbContext</c> in Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<UserRoomScope> UserRoomScopes { get; }
    DbSet<FunctionScreen> FunctionScreens { get; }
    DbSet<RoleScreenPermission> RoleScreenPermissions { get; }
    DbSet<UserScreenPermission> UserScreenPermissions { get; }

    DbSet<Building> Buildings { get; }
    DbSet<Room> Rooms { get; }
    DbSet<Computer> Computers { get; }
    DbSet<ComputerSpec> ComputerSpecs { get; }
    DbSet<ComputerStatus> ComputerStatuses { get; }
    DbSet<ComputerMetric> ComputerMetrics { get; }
    DbSet<AgentPackage> AgentPackages { get; }
    DbSet<TransferFolder> TransferFolders { get; }
    DbSet<FileTransfer> FileTransfers { get; }
    DbSet<CollectedFile> CollectedFiles { get; }

    DbSet<Class> Classes { get; }
    DbSet<Student> Students { get; }
    DbSet<ClassStudent> ClassStudents { get; }
    DbSet<Schedule> Schedules { get; }

    DbSet<Session> Sessions { get; }
    DbSet<SessionComputer> SessionComputers { get; }

    DbSet<Command> Commands { get; }
    DbSet<CommandResult> CommandResults { get; }

    DbSet<ManagedApplication> Applications { get; }
    DbSet<ApplicationPolicy> ApplicationPolicies { get; }
    DbSet<WebsitePolicy> WebsitePolicies { get; }

    DbSet<Screenshot> Screenshots { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
