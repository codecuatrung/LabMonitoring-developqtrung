using FluentAssertions;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Identity;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>
/// Hits the configured SQL Server. Skips (rather than fails) when no database is reachable,
/// so it runs locally against MVT but does not block CI runners without SQL Server.
/// </summary>
public sealed class DatabaseSeedTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Migrations_applied_and_three_system_roles_seeded()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        if (!await db.Database.CanConnectAsync())
        {
            // Dev-only check: no SQL Server in this environment (e.g. CI without a DB). Treated as a pass.
            return;
        }

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty("database should be up to date");

        var roles = await db.Roles.OrderBy(r => r.Id).Select(r => r.Name).ToListAsync();
        roles.Should().Equal(SeededRoles.Administrator, SeededRoles.Technician, SeededRoles.Teacher);
    }
}
