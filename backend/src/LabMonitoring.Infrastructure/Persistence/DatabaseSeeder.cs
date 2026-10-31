using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and, optionally, an idempotent set of development sample data
/// (one building, two lab rooms, a few app/URL policies). Invoked by <c>dotnet run -- seed</c>.
/// </summary>
public sealed class DatabaseSeeder(LabMonitoringDbContext db, ILogger<DatabaseSeeder> logger)
{
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database is up to date, no migrations to apply.");
            return;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
    }

    public async Task SeedDevDataAsync(CancellationToken ct = default)
    {
        if (await db.Buildings.AnyAsync(ct))
        {
            logger.LogInformation("Dev data already present, skipping.");
            return;
        }

        var building = new Building { Name = "Tòa nhà A", Code = "TN-A" };
        db.Buildings.Add(building);

        building.Rooms.Add(new Room { Name = "Phòng thực hành 1", Code = "LAB-01", Capacity = 40 });
        building.Rooms.Add(new Room { Name = "Phòng thực hành 2", Code = "LAB-02", Capacity = 30 });

        var chrome = new ManagedApplication { Name = "Google Chrome", ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe" };
        var vscode = new ManagedApplication { Name = "Visual Studio Code", ExecutablePath = @"C:\Program Files\Microsoft VS Code\Code.exe" };
        var steam = new ManagedApplication { Name = "Steam", ExecutablePath = @"C:\Program Files (x86)\Steam\steam.exe" };
        db.Applications.AddRange(chrome, vscode, steam);

        db.ApplicationPolicies.AddRange(
            new ApplicationPolicy { Application = chrome, PolicyType = PolicyType.Allow },
            new ApplicationPolicy { Application = vscode, PolicyType = PolicyType.Allow },
            new ApplicationPolicy { Application = steam, PolicyType = PolicyType.Block });

        db.WebsitePolicies.AddRange(
            new WebsitePolicy { Domain = "google.com", PolicyType = PolicyType.Allow },
            new WebsitePolicy { Domain = "docs.microsoft.com", PolicyType = PolicyType.Allow },
            new WebsitePolicy { Domain = "facebook.com", PolicyType = PolicyType.Block });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded dev data: 1 building, 2 rooms, {Apps} apps, {Web} website policies.",
            3, 3);
    }
}
