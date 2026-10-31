using LabMonitoring.Application.Agent;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Infrastructure.Agent;
using Microsoft.AspNetCore.DataProtection;
using LabMonitoring.Infrastructure.Monitoring;
using LabMonitoring.Infrastructure.Identity;
using LabMonitoring.Infrastructure.Network;
using LabMonitoring.Infrastructure.Notifications;
using LabMonitoring.Infrastructure.Persistence;
using LabMonitoring.Infrastructure.RemoteControl;
using LabMonitoring.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.Infrastructure;

/// <summary>
/// Wires the Infrastructure layer: EF Core DbContext (SQL Server), identity/provisioning, agent access.
/// Redis / storage / email registrations are added per module (F3 onward).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SqlServer");

        services.AddDbContext<LabMonitoringDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(LabMonitoringDbContext).Assembly.GetName().Name);
                sql.MigrationsHistoryTable("__EFMigrationsHistory");
                // Transient SQL Server errors (deadlock victim, connection blip) retry instead of
                // 500ing the request outright — found via a real, if rare, deadlock between
                // concurrent SaveChanges on Users/UserRoles (M6) and Notifications (M5) during the
                // integration test suite; the same contention can happen in production under
                // concurrent admin actions + heartbeats.
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            }));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<LabMonitoringDbContext>());

        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName));
        services.AddOptions<AgentDefaultsOptions>().Bind(configuration.GetSection(AgentDefaultsOptions.SectionName));
        services.AddOptions<AlertOptions>().Bind(configuration.GetSection(AlertOptions.SectionName));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));

        // Encrypts AgentSecret at rest so the server can sign commands with it later (ADR-0009).
        // Single-instance MVP: keys persist to disk. Scale-out would need a shared key ring
        // (Redis/blob) — see the ADR.
        services.AddDataProtection()
            .SetApplicationName("LabMonitoring")
            .PersistKeysToFileSystem(new DirectoryInfo(ApiPaths.DataProtectionKeyDirectory));
        services.AddSingleton<AgentSecretProtector>();
        services.AddScoped<ICommandSigner, HmacCommandSigner>();
        services.AddSingleton<IWakeOnLanSender, WakeOnLanSender>();
        services.AddSingleton<IRemoteControlSessionRegistry, RemoteControlSessionRegistry>();
        services.AddSingleton<IPresentationSessionRegistry, LabMonitoring.Infrastructure.Presentation.PresentationSessionRegistry>();
        services.AddSingleton<IAgentPackageStore, LocalDiskAgentPackageStore>();
        services.AddOptions<LabMonitoring.Infrastructure.FileTransfers.FileTransferOptions>().Bind(configuration.GetSection(LabMonitoring.Infrastructure.FileTransfers.FileTransferOptions.SectionName));
        services.AddSingleton<IFileTransferPolicy, LabMonitoring.Infrastructure.FileTransfers.FileTransferPolicy>();
        services.AddSingleton<IFileTransferStore, LabMonitoring.Infrastructure.FileTransfers.LocalDiskFileTransferStore>();
        services.AddSingleton<ITransferThrottle, LabMonitoring.Infrastructure.FileTransfers.TransferThrottle>();

        services.AddMemoryCache();
        services.AddScoped<IUserProvisioner, UserProvisioner>();
        services.AddScoped<IRoomScope, RoomScope>();
        services.AddScoped<IScreenAccessService, ScreenAccessService>();
        services.AddScoped<IAgentAccessService, AgentAccessService>();
        services.AddScoped<DatabaseSeeder>();

        // Real SMTP delivery only once Email:Enabled=true; otherwise alerts still fire (realtime +
        // in-app Notification), just not emailed — logged instead. See ADR-0010.
        if (configuration.GetValue<bool>($"{EmailOptions.SectionName}:Enabled"))
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        else
            services.AddScoped<IEmailSender, NullEmailSender>();
        services.AddScoped<IAlertPublisher, AlertPublisher>();

        services.AddHostedService<OfflineDetectorService>();
        services.AddHostedService<CommandTimeoutService>();
        services.AddHostedService<LabMonitoring.Infrastructure.FileTransfers.FileTransferRetentionService>();

        return services;
    }
}
