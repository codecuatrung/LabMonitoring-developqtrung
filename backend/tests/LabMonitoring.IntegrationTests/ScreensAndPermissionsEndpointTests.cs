using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LabMonitoring.Application.Roles;
using LabMonitoring.Application.Screens;
using LabMonitoring.Application.Users;
using LabMonitoring.Domain.Common;
using LabMonitoring.IntegrationTests.TestAuth;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>Role CRUD, screen registry CRUD, role→screen and per-user screen-override permission
/// resolution (SRS FR-8 "màn hình chức năng" / 4-layer permission model). DB-gated, self-cleaning.
/// Shares the non-parallel collection with the other tests that mutate Users/UserRoles system-wide
/// — see <see cref="UserManagementSensitiveCollection"/>.</summary>
[Collection("UserManagementSensitive")]
public sealed class ScreensAndPermissionsEndpointTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAdminClient();
    private bool _db;
    private int _testUserId;
    private static readonly Guid TestUserSub = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        _db = await db.Database.CanConnectAsync();
        if (!_db) return;

        var testClient = factory.CreateClient();
        testClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.ForUser("screen-test-user", "screen-test@lab.local", TestUserSub));
        (await testClient.GetAsync("/api/v1/meta")).EnsureSuccessStatusCode();
        _testUserId = await db.Users.Where(u => u.KeycloakUserId == TestUserSub).Select(u => u.Id).SingleAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_db) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        await db.UserScreenPermissions.Where(p => p.UserId == _testUserId).ExecuteDeleteAsync();
        await db.UserRoles.Where(ur => ur.UserId == _testUserId).ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == _testUserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(a => a.EntityType == "Role" && a.Detail != null && a.Detail.StartsWith("TestRole")).ExecuteDeleteAsync();
        await db.RoleScreenPermissions.Where(p => db.Roles.Any(r => r.Id == p.RoleId && r.Name.StartsWith("TestRole"))).ExecuteDeleteAsync();
        await db.Roles.Where(r => r.Name.StartsWith("TestRole")).ExecuteDeleteAsync();
        await db.AuditLogs.Where(a => a.EntityType == "FunctionScreen" && a.Detail == "test.screen").ExecuteDeleteAsync();
        await db.FunctionScreens.Where(s => s.Code == "test.screen").ExecuteDeleteAsync();
    }

    private HttpClient TestUserClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.ForUser("screen-test-user", "screen-test@lab.local", TestUserSub));
        return client;
    }

    [Fact]
    public async Task Role_crud_lifecycle()
    {
        if (!_db) return;

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var createRes = await _client.PostAsJsonAsync("/api/v1/roles", new { name = $"TestRole-{suffix}" });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var roleId = (await createRes.Content.ReadFromJsonAsync<IdResult>())!.Id;

        var list = await _client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        list!.Should().Contain(r => r.Id == roleId && r.Name == $"TestRole-{suffix}" && !r.IsSystem);

        (await _client.PutAsJsonAsync($"/api/v1/roles/{roleId}", new { name = $"TestRole-{suffix}-renamed" }))
            .EnsureSuccessStatusCode();

        (await _client.DeleteAsync($"/api/v1/roles/{roleId}")).EnsureSuccessStatusCode();
        var afterDelete = await _client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        afterDelete!.Should().NotContain(r => r.Id == roleId);
    }

    [Fact]
    public async Task System_roles_cannot_be_renamed_or_deleted()
    {
        if (!_db) return;

        var rename = await _client.PutAsJsonAsync($"/api/v1/roles/{SeededRoles.AdministratorId}", new { name = "Hacked" });
        rename.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var delete = await _client.DeleteAsync($"/api/v1/roles/{SeededRoles.TechnicianId}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Screen_crud_lifecycle()
    {
        if (!_db) return;

        var createRes = await _client.PostAsJsonAsync("/api/v1/screens",
            new { code = "test.screen", name = "Test Screen", route = "/test-screen", icon = (string?)null, displayOrder = 99 });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var screenId = (await createRes.Content.ReadFromJsonAsync<IdResult>())!.Id;

        var list = await _client.GetFromJsonAsync<List<ScreenDto>>("/api/v1/screens");
        list!.Should().Contain(s => s.Id == screenId && s.Code == "test.screen");

        (await _client.PutAsJsonAsync($"/api/v1/screens/{screenId}",
            new { name = "Test Screen Updated", route = "/test-screen", icon = (string?)null, displayOrder = 99, isActive = true }))
            .EnsureSuccessStatusCode();

        (await _client.DeleteAsync($"/api/v1/screens/{screenId}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Custom_role_screen_permission_controls_api_access()
    {
        if (!_db) return;

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var roleId = (await (await _client.PostAsJsonAsync("/api/v1/roles", new { name = $"TestRole-{suffix}" }))
            .Content.ReadFromJsonAsync<IdResult>())!.Id;

        // Grant only "audit-logs" — nothing else, not even the AdminConsole-open screens.
        (await _client.PutAsJsonAsync($"/api/v1/roles/{roleId}/screens", new[] { SeededScreens.AuditLogsId }))
            .EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync($"/api/v1/users/{_testUserId}/roles", new[] { roleId })).EnsureSuccessStatusCode();

        var testClient = TestUserClient();
        (await testClient.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await testClient.GetAsync("/api/v1/classes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Cleanup (removing the test user's role, then the role itself) happens in DisposeAsync.
    }

    [Fact]
    public async Task User_screen_override_grant_and_revoke_take_precedence_over_role()
    {
        if (!_db) return;

        // Technician has "classes" but not "audit-logs" per the seeded defaults (ADR-0010/this session).
        (await _client.PutAsJsonAsync($"/api/v1/users/{_testUserId}/roles", new[] { SeededRoles.TechnicianId }))
            .EnsureSuccessStatusCode();

        var testClient = TestUserClient();
        (await testClient.GetAsync("/api/v1/classes")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await testClient.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Grant override for audit-logs (role doesn't have it) and revoke override for classes (role has it).
        (await _client.PutAsJsonAsync($"/api/v1/users/{_testUserId}/screen-overrides", new[]
        {
            new { screenId = SeededScreens.AuditLogsId, isGranted = true },
            new { screenId = SeededScreens.ClassesId, isGranted = false },
        })).EnsureSuccessStatusCode();

        testClient = TestUserClient();
        (await testClient.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await testClient.GetAsync("/api/v1/classes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var detail = await _client.GetFromJsonAsync<UserDetailDto>($"/api/v1/users/{_testUserId}");
        detail!.ScreenOverrides.Should().Contain(o => o.ScreenId == SeededScreens.AuditLogsId && o.IsGranted);
        detail.ScreenOverrides.Should().Contain(o => o.ScreenId == SeededScreens.ClassesId && !o.IsGranted);
    }

    [Fact]
    public async Task My_screens_endpoint_reflects_resolved_access_for_any_authenticated_user()
    {
        if (!_db) return;

        (await _client.PutAsJsonAsync($"/api/v1/users/{_testUserId}/roles", new[] { SeededRoles.TeacherId }))
            .EnsureSuccessStatusCode();

        var testClient = TestUserClient();
        var mine = await testClient.GetFromJsonAsync<List<ScreenDto>>("/api/v1/me/screens");
        mine!.Should().Contain(s => s.Code == SeededScreens.Dashboard);
        mine.Should().NotContain(s => s.Code == SeededScreens.Users); // Teacher doesn't get Users
    }

    private sealed record IdResult(int Id);
}
