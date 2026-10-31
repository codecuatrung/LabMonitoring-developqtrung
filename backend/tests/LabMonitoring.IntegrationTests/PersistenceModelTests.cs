using FluentAssertions;
using LabMonitoring.Domain.Commands;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>
/// Validates the EF Core model without touching a database — building the model and reading
/// metadata never opens a connection.
/// </summary>
public sealed class PersistenceModelTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private LabMonitoringDbContext CreateContext()
        => factory.Services.CreateScope().ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

    [Fact]
    public void Model_maps_all_31_tables()
    {
        using var ctx = CreateContext();

        var tables = ctx.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => n is not null)
            .Distinct()
            .ToList();

        // 24 from the original SRS ERD (F1) + FunctionScreens/RoleScreenPermissions/
        // UserScreenPermissions (SRS FR-8 role/screen/user-override permission model, added later).
        tables.Should().HaveCount(31);
        tables.Should().Contain(["Users", "Roles", "Rooms", "Computers", "ComputerMetrics",
            "Sessions", "Commands", "CommandResults", "Screenshots", "AuditLogs", "Notifications",
            "FunctionScreens", "RoleScreenPermissions", "UserScreenPermissions"]);
    }

    [Fact]
    public void Command_nonce_has_a_unique_index()
    {
        using var ctx = CreateContext();

        var nonceIndex = ctx.Model.FindEntityType(typeof(Command))!
            .GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Command.Nonce)));

        nonceIndex.Should().NotBeNull();
        nonceIndex!.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Status_enums_are_stored_as_strings()
    {
        using var ctx = CreateContext();

        var statusProp = ctx.Model.FindEntityType(typeof(ComputerStatus))!
            .FindProperty(nameof(ComputerStatus.CurrentStatus))!;

        statusProp.GetProviderClrType().Should().Be<string>();
        statusProp.GetMaxLength().Should().Be(20);
    }
}
