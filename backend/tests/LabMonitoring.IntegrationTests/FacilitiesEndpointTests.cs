using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Application.Dashboard;
using LabMonitoring.Application.Facilities;
using LabMonitoring.IntegrationTests.TestAuth;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>CRUD + approval + dashboard for FR-1/FR-5. Hits SQL Server; self-cleans; skips without a DB.</summary>
public sealed class FacilitiesEndpointTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAdminClient();
    private readonly List<int> _buildingIds = [];
    private bool _db;

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        _db = await scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>().Database.CanConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_db) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        foreach (var b in _buildingIds)
        {
            var roomIds = await db.Rooms.Where(r => r.BuildingId == b).Select(r => r.Id).ToListAsync();
            await db.Computers.Where(c => roomIds.Contains(c.RoomId)).ExecuteDeleteAsync();
            await db.Rooms.Where(r => r.BuildingId == b).ExecuteDeleteAsync();
            await db.Buildings.Where(x => x.Id == b).ExecuteDeleteAsync();
        }
    }

    private async Task<int> NewBuildingAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var res = await _client.PostAsJsonAsync("/api/v1/buildings", new { name = $"IT {suffix}", code = $"B-{suffix}" });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await res.Content.ReadFromJsonAsync<IdResult>())!.Id;
        _buildingIds.Add(id);
        return id;
    }

    [Fact]
    public async Task Anonymous_request_is_unauthorized()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/buildings");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Building_then_room_crud_roundtrip()
    {
        if (!_db) return;
        var buildingId = await NewBuildingAsync();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var roomRes = await _client.PostAsJsonAsync("/api/v1/rooms",
            new { buildingId, name = $"Lab {suffix}", code = $"R-{suffix}", capacity = 25, mapImageUrl = (string?)null });
        roomRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var roomId = (await roomRes.Content.ReadFromJsonAsync<IdResult>())!.Id;

        var detail = await _client.GetFromJsonAsync<RoomDetailDto>($"/api/v1/rooms/{roomId}");
        detail!.Capacity.Should().Be(25);
        detail.Computers.Should().BeEmpty();

        var list = await _client.GetFromJsonAsync<PagedResult<RoomDto>>($"/api/v1/rooms?buildingId={buildingId}");
        list!.Items.Should().ContainSingle(r => r.Id == roomId);
    }

    [Fact]
    public async Task Register_a_machine_then_approve_it()
    {
        if (!_db) return;
        var buildingId = await NewBuildingAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var roomCode = $"R-{suffix}";
        var roomRes = await _client.PostAsJsonAsync("/api/v1/rooms",
            new { buildingId, name = $"Lab {suffix}", code = roomCode, capacity = 10, mapImageUrl = (string?)null });
        var roomId = (await roomRes.Content.ReadFromJsonAsync<IdResult>())!.Id;

        // Agent self-registers (anonymous endpoint).
        var reg = await factory.CreateClient().PostAsJsonAsync("/api/v1/agent/register", new
        {
            machineId = Guid.NewGuid(), roomCode, hostname = "pc-approve-01",
        });
        reg.EnsureSuccessStatusCode();

        var pending = await _client.GetFromJsonAsync<List<RoomComputerDto>>("/api/v1/computers?pendingApproval=true");
        var mine = pending!.Single(c => c.Name == "PC-APPROVE-01");
        mine.IsApproved.Should().BeFalse();

        var approve = await _client.PostAsync($"/api/v1/computers/{mine.Id}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDetail = await _client.GetFromJsonAsync<ComputerDetailDto>($"/api/v1/computers/{mine.Id}");
        afterDetail!.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Dashboard_summary_returns_counts()
    {
        if (!_db) return;
        var summary = await _client.GetFromJsonAsync<DashboardSummaryDto>("/api/v1/dashboard/summary");
        summary!.Rooms.Should().BeGreaterThanOrEqualTo(0);
        (summary.Online + summary.Offline + summary.Warning + summary.InUse).Should().Be(summary.Computers);
    }

    private sealed record IdResult(int Id);
}
