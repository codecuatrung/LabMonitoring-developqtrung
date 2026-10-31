using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LabMonitoring.Contracts.Agent;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>
/// Exercises /api/v1/agent/register + /config against SQL Server. Creates its own building/room
/// and removes every row it added. Skips when no database is reachable.
/// </summary>
public sealed class AgentEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _roomCode = "TEST-" + Guid.NewGuid().ToString("N")[..8];
    private int _buildingId;
    private int _roomId;
    private bool _dbAvailable;

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        _dbAvailable = await db.Database.CanConnectAsync();
        if (!_dbAvailable) return;

        var building = new Building { Name = "Test Building", Code = "TB-" + Guid.NewGuid().ToString("N")[..6] };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();
        _buildingId = building.Id;

        var room = new Room { BuildingId = _buildingId, Name = "Test Room", Code = _roomCode, Capacity = 30 };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        _roomId = room.Id;
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        await db.Computers.Where(c => c.RoomId == _roomId).ExecuteDeleteAsync();
        await db.Rooms.Where(r => r.Id == _roomId).ExecuteDeleteAsync();
        await db.Buildings.Where(b => b.Id == _buildingId).ExecuteDeleteAsync();
    }

    private AgentRegisterRequest NewRequest(Guid? machineId = null) => new()
    {
        MachineId = machineId ?? Guid.NewGuid(),
        RoomCode = _roomCode,
        Hostname = "pc-test-01",
        AgentVersion = "1.0.0",
        CpuModel = "Test CPU",
        CpuCores = 4,
        RamTotalMB = 8192,
        DiskTotalGB = 256,
        OsVersion = "Windows 11",
    };

    [Fact]
    public async Task Register_then_get_config_with_the_issued_token()
    {
        if (!_dbAvailable) return;
        var client = factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/v1/agent/register", NewRequest());
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = (await register.Content.ReadFromJsonAsync<AgentRegisterResponse>())!;
        body.AgentToken.Should().NotBeNullOrWhiteSpace();
        body.AgentSecret.Should().NotBeNullOrWhiteSpace();
        body.Approved.Should().BeFalse();

        var configReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/agent/config");
        configReq.Headers.Authorization = new AuthenticationHeaderValue("AgentToken", body.AgentToken);
        var config = await client.SendAsync(configReq);

        config.StatusCode.Should().Be(HttpStatusCode.OK);
        var cfg = (await config.Content.ReadFromJsonAsync<AgentConfigResponse>())!;
        cfg.HeartbeatSeconds.Should().BeGreaterThan(0);
        cfg.OfflineThresholdSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Heartbeat_updates_status_and_appends_a_metric()
    {
        if (!_dbAvailable) return;
        var client = factory.CreateClient();

        var reg = (await (await client.PostAsJsonAsync("/api/v1/agent/register", NewRequest()))
            .Content.ReadFromJsonAsync<AgentRegisterResponse>())!;

        var hb = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/heartbeat")
        {
            Content = JsonContent.Create(new AgentHeartbeatRequest
            {
                Status = "Online",
                CurrentWindowsUser = "sv001",
                CpuUsagePercent = 12.5m,
                RamUsagePercent = 47.0m,
                DiskUsagePercent = 60.0m,
            }),
        };
        hb.Headers.Authorization = new AuthenticationHeaderValue("AgentToken", reg.AgentToken);
        var response = await client.SendAsync(hb);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        var status = await db.ComputerStatuses.SingleAsync(s => s.ComputerId == reg.ComputerId);
        status.CurrentWindowsUser.Should().Be("sv001");
        status.LastHeartbeatAt.Should().NotBeNull();
        (await db.ComputerMetrics.CountAsync(m => m.ComputerId == reg.ComputerId)).Should().Be(1);
    }

    [Fact]
    public async Task Register_is_rejected_for_an_unknown_room_code()
    {
        if (!_dbAvailable) return;
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/agent/register",
            NewRequest() with { RoomCode = "NO-SUCH-ROOM" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registering_the_same_machine_twice_is_a_conflict()
    {
        if (!_dbAvailable) return;
        var client = factory.CreateClient();
        var machineId = Guid.NewGuid();

        (await client.PostAsJsonAsync("/api/v1/agent/register", NewRequest(machineId))).EnsureSuccessStatusCode();
        var second = await client.PostAsJsonAsync("/api/v1/agent/register", NewRequest(machineId));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Config_without_an_agent_token_is_unauthorized()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/agent/config");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
