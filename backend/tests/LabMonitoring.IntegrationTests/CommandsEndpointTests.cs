using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LabMonitoring.Application.Commands;
using LabMonitoring.Contracts.Agent;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Policies;
using LabMonitoring.IntegrationTests.TestAuth;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>SRS FR-4: signed commands, whitelist enforcement, two-step room confirm. DB-gated, self-cleaning.</summary>
public sealed class CommandsEndpointTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAdminClient();
    private bool _db;
    private int _buildingId, _roomId, _appId;

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

        var app = new ManagedApplication { Name = "Test Notepad", ExecutablePath = @"C:\Windows\notepad.exe" };
        db.Applications.Add(app);
        await db.SaveChangesAsync();
        _appId = app.Id;

        db.ApplicationPolicies.Add(new ApplicationPolicy { RoomId = _roomId, ApplicationId = _appId, PolicyType = PolicyType.Allow });
        db.WebsitePolicies.Add(new WebsitePolicy { RoomId = _roomId, Domain = "allowed-test.example", PolicyType = PolicyType.Allow });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_db) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        var computerIds = await db.Computers.Where(c => c.RoomId == _roomId).Select(c => c.Id).ToListAsync();
        await db.CommandResults.Where(r => computerIds.Contains(r.ComputerId)).ExecuteDeleteAsync();
        await db.Commands.Where(c => c.RoomId == _roomId || (c.ComputerId != null && computerIds.Contains(c.ComputerId.Value))).ExecuteDeleteAsync();
        await db.Computers.Where(c => c.RoomId == _roomId).ExecuteDeleteAsync();
        await db.ApplicationPolicies.Where(p => p.RoomId == _roomId).ExecuteDeleteAsync();
        await db.WebsitePolicies.Where(p => p.RoomId == _roomId).ExecuteDeleteAsync();
        await db.Applications.Where(a => a.Id == _appId).ExecuteDeleteAsync();
        await db.Rooms.Where(r => r.Id == _roomId).ExecuteDeleteAsync();
        await db.Buildings.Where(b => b.Id == _buildingId).ExecuteDeleteAsync();
    }

    private async Task<(int computerId, string agentToken, string agentSecret)> RegisterApprovedComputerAsync(string hostname)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        var roomCode = await db.Rooms.Where(r => r.Id == _roomId).Select(r => r.Code).SingleAsync();

        var reg = await factory.CreateClient().PostAsJsonAsync("/api/v1/agent/register",
            new { machineId = Guid.NewGuid(), roomCode, hostname });
        var body = (await reg.Content.ReadFromJsonAsync<AgentRegisterResponse>())!;
        (await _client.PostAsync($"/api/v1/computers/{body.ComputerId}/approve", null)).EnsureSuccessStatusCode();
        return (body.ComputerId, body.AgentToken, body.AgentSecret);
    }

    [Fact]
    public async Task Single_machine_command_is_sent_and_signed_with_that_machines_secret()
    {
        if (!_db) return;
        var (computerId, _, agentSecret) = await RegisterApprovedComputerAsync("pc-cmd-01");

        var issue = await _client.PostAsJsonAsync("/api/v1/commands",
            new { computerId, roomId = (int?)null, sessionId = (int?)null, commandType = "Lock", confirm = false });
        issue.StatusCode.Should().Be(HttpStatusCode.Created);
        var issued = (await issue.Content.ReadFromJsonAsync<IssueCommandResultDto>())!;
        issued.TargetCount.Should().Be(1);

        var results = await _client.GetFromJsonAsync<List<CommandResultDto>>($"/api/v1/commands/{issued.CommandId}/results");
        results.Should().ContainSingle(r => r.ComputerId == computerId && r.Status == "Sent");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        var command = await db.Commands.SingleAsync(c => c.Id == issued.CommandId);
        var expectedSignature = Convert.ToBase64String(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(agentSecret), Encoding.UTF8.GetBytes(command.Payload!)));
        command.Signature.Should().Be(expectedSignature, "the server must sign with THIS machine's own AgentSecret (SRS 8.5)");
    }

    [Fact]
    public async Task Agent_reports_a_command_result_via_rest()
    {
        if (!_db) return;
        var (computerId, agentToken, _) = await RegisterApprovedComputerAsync("pc-cmd-02");

        var issue = await _client.PostAsJsonAsync("/api/v1/commands",
            new { computerId, roomId = (int?)null, sessionId = (int?)null, commandType = "Message", text = "hi", confirm = false });
        var issued = (await issue.Content.ReadFromJsonAsync<IssueCommandResultDto>())!;

        var resultReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/command-result")
        {
            Content = JsonContent.Create(new AgentCommandResultRequest
            {
                CommandId = issued.CommandId,
                Status = CommandExecutionStatus.Executed,
                ErrorMessage = null,
            }),
        };
        resultReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("AgentToken", agentToken);
        (await factory.CreateClient().SendAsync(resultReq)).EnsureSuccessStatusCode();

        var results = await _client.GetFromJsonAsync<List<CommandResultDto>>($"/api/v1/commands/{issued.CommandId}/results");
        var result = results!.Single(r => r.ComputerId == computerId);
        result.Status.Should().Be("Executed");
        result.ExecutedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Room_wide_restart_requires_confirm_then_targets_every_approved_machine()
    {
        if (!_db) return;
        await RegisterApprovedComputerAsync("pc-cmd-03");
        await RegisterApprovedComputerAsync("pc-cmd-04");

        var noConfirm = await _client.PostAsJsonAsync("/api/v1/commands",
            new { computerId = (int?)null, roomId = _roomId, sessionId = (int?)null, commandType = "Restart", confirm = false });
        noConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest, "FR-4.3 requires two-step confirmation for a room-wide reboot");

        var confirmed = await _client.PostAsJsonAsync("/api/v1/commands",
            new { computerId = (int?)null, roomId = _roomId, sessionId = (int?)null, commandType = "Restart", confirm = true });
        confirmed.StatusCode.Should().Be(HttpStatusCode.Created);
        var issued = (await confirmed.Content.ReadFromJsonAsync<IssueCommandResultDto>())!;
        issued.TargetCount.Should().Be(2);
    }

    [Fact]
    public async Task Open_app_outside_the_whitelist_is_forbidden_but_the_allowed_one_succeeds()
    {
        if (!_db) return;
        var (computerId, _, _) = await RegisterApprovedComputerAsync("pc-cmd-05");

        var blocked = await _client.PostAsJsonAsync("/api/v1/commands", new
        {
            computerId, roomId = (int?)null, sessionId = (int?)null, commandType = "OpenApp",
            executablePath = @"C:\Windows\System32\cmd.exe", confirm = false,
        });
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var allowed = await _client.PostAsJsonAsync("/api/v1/commands", new
        {
            computerId, roomId = (int?)null, sessionId = (int?)null, commandType = "OpenApp",
            executablePath = @"C:\Windows\notepad.exe", confirm = false,
        });
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Open_url_outside_the_whitelist_is_forbidden_but_the_allowed_domain_succeeds()
    {
        if (!_db) return;
        var (computerId, _, _) = await RegisterApprovedComputerAsync("pc-cmd-06");

        var blocked = await _client.PostAsJsonAsync("/api/v1/commands", new
        {
            computerId, roomId = (int?)null, sessionId = (int?)null, commandType = "OpenUrl",
            url = "https://not-allowed.example/", confirm = false,
        });
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var allowed = await _client.PostAsJsonAsync("/api/v1/commands", new
        {
            computerId, roomId = (int?)null, sessionId = (int?)null, commandType = "OpenUrl",
            url = "https://allowed-test.example/path", confirm = false,
        });
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
