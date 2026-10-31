using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LabMonitoring.Application.Facilities;
using LabMonitoring.Application.Notifications;
using LabMonitoring.Application.Reports;
using LabMonitoring.Contracts.Agent;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.IntegrationTests.TestAuth;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>M5 — FR-7: CPU/RAM alert engine (in-app Notification, email fallback), usage report.
/// DB-gated, self-cleaning. See <see cref="UserManagementSensitiveCollection"/> for why the CPU
/// alert test shares a non-parallel collection with <see cref="UserManagementAndAuditEndpointTests"/>.</summary>
[Collection("UserManagementSensitive")]
public sealed class NotificationsAndReportsEndpointTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAdminClient();
    private bool _db;
    private int _buildingId, _roomId;

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        _db = await db.Database.CanConnectAsync();
        if (!_db) return;

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var building = new Building { Name = $"IT {suffix}", Code = $"B-{suffix}" };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();
        _buildingId = building.Id;

        var room = new Room { BuildingId = _buildingId, Name = $"Lab {suffix}", Code = $"R-{suffix}", Capacity = 10 };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        _roomId = room.Id;
    }

    public async Task DisposeAsync()
    {
        if (!_db) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        // Alert notifications reference the computer by name in the message, not by a foreign key —
        // match on the fixed test hostname (PC-ALERT-01, per DeriveName's ToUpperInvariant).
        await db.Notifications.Where(n => n.Message.Contains("PC-ALERT-01")).ExecuteDeleteAsync();
        await db.Sessions.Where(s => s.RoomId == _roomId).ExecuteDeleteAsync();
        await db.Computers.Where(c => c.RoomId == _roomId).ExecuteDeleteAsync();
        await db.Rooms.Where(r => r.Id == _roomId).ExecuteDeleteAsync();
        await db.Buildings.Where(b => b.Id == _buildingId).ExecuteDeleteAsync();
    }

    private async Task<(int computerId, string agentToken)> RegisterApprovedComputerAsync(string roomCode)
    {
        var reg = await factory.CreateClient().PostAsJsonAsync("/api/v1/agent/register", new
        {
            machineId = Guid.NewGuid(),
            roomCode,
            hostname = "pc-alert-01",
        });
        var body = (await reg.Content.ReadFromJsonAsync<AgentRegisterResponse>())!;
        (await _client.PostAsync($"/api/v1/computers/{body.ComputerId}/approve", null)).EnsureSuccessStatusCode();
        return (body.ComputerId, body.AgentToken);
    }

    [Fact]
    public async Task Cpu_over_threshold_creates_an_in_app_notification_for_the_administrator()
    {
        if (!_db) return;

        var room = await _client.GetFromJsonAsync<RoomDetailDto>($"/api/v1/rooms/{_roomId}");
        var (computerId, agentToken) = await RegisterApprovedComputerAsync(room!.Code);

        var before = await _client.GetFromJsonAsync<int>("/api/v1/notifications/unread-count");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/heartbeat")
        {
            Content = JsonContent.Create(new AgentHeartbeatRequest { Status = "Warning", CpuUsagePercent = 95m }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("AgentToken", agentToken);
        (await factory.CreateClient().SendAsync(req)).EnsureSuccessStatusCode();

        var after = await _client.GetFromJsonAsync<int>("/api/v1/notifications/unread-count");
        after.Should().BeGreaterThan(before);

        var list = await _client.GetFromJsonAsync<List<NotificationDto>>("/api/v1/notifications?unreadOnly=true&limit=200");
        var mine = list!.Should().Contain(n => n.Title == "CPU vượt ngưỡng").Which;

        (await _client.PostAsync($"/api/v1/notifications/{mine.Id}/read", null)).EnsureSuccessStatusCode();
        var afterRead = await _client.GetFromJsonAsync<List<NotificationDto>>("/api/v1/notifications?unreadOnly=true&limit=200");
        afterRead!.Should().NotContain(n => n.Id == mine.Id);

        // A second breaching heartbeat while still over threshold must not alert again (cooldown
        // until recovery) — proves the CpuAlertRaisedAt gate, not just that the endpoint works once.
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/heartbeat")
        {
            Content = JsonContent.Create(new AgentHeartbeatRequest { Status = "Warning", CpuUsagePercent = 96m }),
        };
        req2.Headers.Authorization = new AuthenticationHeaderValue("AgentToken", agentToken);
        (await factory.CreateClient().SendAsync(req2)).EnsureSuccessStatusCode();

        var afterSecond = await _client.GetFromJsonAsync<List<NotificationDto>>("/api/v1/notifications?unreadOnly=true&limit=200");
        afterSecond!.Count(n => n.Title == "CPU vượt ngưỡng").Should().Be(0); // still just the one we already read, none re-raised
    }

    [Fact]
    public async Task Usage_report_and_csv_export_reflect_the_filtered_range()
    {
        if (!_db) return;

        var suffix = Guid.NewGuid().ToString("N")[..6];
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
            var adminUserId = await db.Users.Where(u => u.KeycloakUserId == AdminApiFactory.AdminSub).Select(u => u.Id).SingleAsync();

            var classRes = await _client.PostAsJsonAsync("/api/v1/classes",
                new { code = $"CLS-{suffix}", name = "Test", subject = "CNTT", teacherId = adminUserId, semester = (string?)null });
            var classId = (await classRes.Content.ReadFromJsonAsync<IdResult>())!.Id;

            var room = await _client.GetFromJsonAsync<RoomDetailDto>($"/api/v1/rooms/{_roomId}");
            await RegisterApprovedComputerAsync(room!.Code);

            var startRes = await _client.PostAsJsonAsync("/api/v1/sessions/start", new { roomId = _roomId, classId, scheduleId = (int?)null });
            var sessionId = (await startRes.Content.ReadFromJsonAsync<IdResult>())!.Id;
            (await _client.PostAsync($"/api/v1/sessions/{sessionId}/end", null)).EnsureSuccessStatusCode();

            var report = await _client.GetFromJsonAsync<UsageReportDto>($"/api/v1/reports/usage?roomId={_roomId}");
            report!.TotalSessions.Should().Be(1);
            report.Rows.Should().ContainSingle(r => r.RoomId == _roomId && r.SessionCount == 1);

            var csv = await _client.GetAsync($"/api/v1/reports/usage/export?roomId={_roomId}");
            csv.StatusCode.Should().Be(HttpStatusCode.OK);
            csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

            await db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            await db.Classes.Where(c => c.Id == classId).ExecuteDeleteAsync();
        }
    }

    private sealed record IdResult(int Id);
}
