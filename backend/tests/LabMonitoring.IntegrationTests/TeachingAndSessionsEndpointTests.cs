using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LabMonitoring.Application.Facilities;
using LabMonitoring.Application.Sessions;
using LabMonitoring.Application.Teaching;
using LabMonitoring.Contracts.Agent;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.IntegrationTests.TestAuth;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>FR-6 (classes/schedules/sessions) + FR-2 (student login/logout via heartbeat). DB-gated, self-cleaning.</summary>
public sealed class TeachingAndSessionsEndpointTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAdminClient();
    private bool _db;
    private int _buildingId, _roomId, _classId, _studentId;

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        _db = await db.Database.CanConnectAsync();
        if (!_db) return;

        // Trigger provisioning so we have a local admin user id to use as the class's teacher.
        (await _client.GetAsync("/api/v1/buildings")).EnsureSuccessStatusCode();
        var adminUserId = await db.Users.Where(u => u.KeycloakUserId == AdminApiFactory.AdminSub).Select(u => u.Id).SingleAsync();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var building = new Building { Name = $"IT {suffix}", Code = $"B-{suffix}" };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();
        _buildingId = building.Id;

        var room = new Room { BuildingId = _buildingId, Name = $"Lab {suffix}", Code = $"R-{suffix}", Capacity = 10 };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        _roomId = room.Id;

        var classRes = await _client.PostAsJsonAsync("/api/v1/classes",
            new { code = $"CLS-{suffix}", name = "Lập trình C#", subject = "CNTT", teacherId = adminUserId, semester = (string?)null });
        classRes.StatusCode.Should().Be(HttpStatusCode.Created);
        _classId = (await classRes.Content.ReadFromJsonAsync<IdResult>())!.Id;

        var studentRes = await _client.PostAsJsonAsync("/api/v1/students",
            new { studentCode = $"SV-{suffix}", fullName = "Nguyen Van A", email = (string?)null });
        _studentId = (await studentRes.Content.ReadFromJsonAsync<IdResult>())!.Id;
        (await _client.PostAsync($"/api/v1/classes/{_classId}/students/{_studentId}", null)).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        if (!_db) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        await db.Sessions.Where(s => s.RoomId == _roomId).ExecuteDeleteAsync(); // cascades SessionComputers
        await db.Computers.Where(c => c.RoomId == _roomId).ExecuteDeleteAsync();
        await db.ClassStudents.Where(cs => cs.ClassId == _classId).ExecuteDeleteAsync();
        await db.Students.Where(s => s.Id == _studentId).ExecuteDeleteAsync();
        await db.Classes.Where(c => c.Id == _classId).ExecuteDeleteAsync();
        await db.Rooms.Where(r => r.Id == _roomId).ExecuteDeleteAsync();
        await db.Buildings.Where(b => b.Id == _buildingId).ExecuteDeleteAsync();
    }

    private async Task<(int computerId, string agentToken)> RegisterApprovedComputerAsync(string roomCode)
    {
        var reg = await factory.CreateClient().PostAsJsonAsync("/api/v1/agent/register", new
        {
            machineId = Guid.NewGuid(),
            roomCode,
            hostname = "pc-session-01",
        });
        var body = (await reg.Content.ReadFromJsonAsync<AgentRegisterResponse>())!;
        (await _client.PostAsync($"/api/v1/computers/{body.ComputerId}/approve", null)).EnsureSuccessStatusCode();
        return (body.ComputerId, body.AgentToken);
    }

    [Fact]
    public async Task Full_session_lifecycle_with_allocation_and_login_tracking()
    {
        if (!_db) return;

        var roomCode = await GetRoomCodeAsync();
        var (computerId, agentToken) = await RegisterApprovedComputerAsync(roomCode);

        // Start
        var startRes = await _client.PostAsJsonAsync("/api/v1/sessions/start", new { roomId = _roomId, classId = _classId, scheduleId = (int?)null });
        startRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = (await startRes.Content.ReadFromJsonAsync<IdResult>())!.Id;

        var starting = await _client.GetFromJsonAsync<SessionDetailDto>($"/api/v1/sessions/{sessionId}");
        starting!.Status.Should().Be("Active");
        starting.Computers.Should().ContainSingle(c => c.ComputerId == computerId);

        // Starting a second session in the same room must fail (one active session per room).
        var conflict = await _client.PostAsJsonAsync("/api/v1/sessions/start", new { roomId = _roomId, classId = _classId, scheduleId = (int?)null });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Allocate (auto)
        (await _client.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/allocate", new { auto = true, assignments = (object?)null }))
            .EnsureSuccessStatusCode();
        var allocated = await _client.GetFromJsonAsync<SessionDetailDto>($"/api/v1/sessions/{sessionId}");
        allocated!.Computers.Single(c => c.ComputerId == computerId).StudentId.Should().Be(_studentId);

        // Heartbeat with a Windows user logged in -> SessionComputers.LoginAt set (FR-2.1/2.2)
        await SendHeartbeatAsync(agentToken, windowsUser: "sv001");
        var loggedIn = await _client.GetFromJsonAsync<SessionDetailDto>($"/api/v1/sessions/{sessionId}");
        loggedIn!.Computers.Single(c => c.ComputerId == computerId).LoginAt.Should().NotBeNull();

        // Heartbeat with no user -> LogoutAt set
        await SendHeartbeatAsync(agentToken, windowsUser: null);
        var loggedOut = await _client.GetFromJsonAsync<SessionDetailDto>($"/api/v1/sessions/{sessionId}");
        loggedOut!.Computers.Single(c => c.ComputerId == computerId).LogoutAt.Should().NotBeNull();

        // End
        (await _client.PostAsync($"/api/v1/sessions/{sessionId}/end", null)).EnsureSuccessStatusCode();
        var ended = await _client.GetFromJsonAsync<SessionDetailDto>($"/api/v1/sessions/{sessionId}");
        ended!.Status.Should().Be("Ended");
        ended.EndedAt.Should().NotBeNull();

        // Report (FR-6.4)
        var report = await _client.GetFromJsonAsync<SessionReportDto>($"/api/v1/sessions/{sessionId}/report");
        report!.StudentsEnrolled.Should().Be(1);
        report.ComputersUsed.Should().Be(1);
        report.DurationMinutes.Should().BeGreaterThanOrEqualTo(0);
    }

    private async Task<string> GetRoomCodeAsync()
    {
        var room = await _client.GetFromJsonAsync<RoomDetailDto>($"/api/v1/rooms/{_roomId}");
        return room!.Code;
    }

    private async Task SendHeartbeatAsync(string agentToken, string? windowsUser)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/heartbeat")
        {
            Content = JsonContent.Create(new AgentHeartbeatRequest { Status = "Online", CurrentWindowsUser = windowsUser }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("AgentToken", agentToken);
        (await factory.CreateClient().SendAsync(req)).EnsureSuccessStatusCode();
    }

    private sealed record IdResult(int Id);
}
