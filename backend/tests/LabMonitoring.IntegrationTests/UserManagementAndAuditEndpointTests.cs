using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LabMonitoring.Application.AuditLogs;
using LabMonitoring.Application.Facilities;
using LabMonitoring.Application.Users;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.IntegrationTests.TestAuth;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>
/// xUnit runs different test classes in parallel by default. This one and
/// <see cref="NotificationsAndReportsEndpointTests"/> both touch the real, shared `Users`/`UserRoles`
/// tables system-wide (this one briefly promotes then deletes a throwaway admin; that one's alert
/// test fans a Notification out to every currently-active Administrator) — run them one at a time so
/// a user this test deletes can't vanish mid-query from the other's recipient lookup.
/// </summary>
[CollectionDefinition("UserManagementSensitive", DisableParallelization = true)]
public sealed class UserManagementSensitiveCollection;

/// <summary>M6 — FR-8: user/role/room-scope management, last-Administrator guard, room registration
/// code rotation, audit log search. DB-gated, self-cleaning.</summary>
[Collection("UserManagementSensitive")]
public sealed class UserManagementAndAuditEndpointTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAdminClient();
    private bool _db;
    private int _buildingId, _roomId, _secondUserId;
    private static readonly Guid SecondUserSub = Guid.Parse("22222222-2222-2222-2222-222222222222");

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

        // Provision a second, non-admin account so role/scope changes and the last-Administrator
        // guard can be tested without touching the shared bootstrap admin's own account.
        var secondClient = factory.CreateClient();
        secondClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.ForUser("second-user", "second@lab.local", SecondUserSub));
        (await secondClient.GetAsync("/api/v1/meta")).EnsureSuccessStatusCode();
        _secondUserId = await db.Users.Where(u => u.KeycloakUserId == SecondUserSub).Select(u => u.Id).SingleAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_db) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        await db.AuditLogs.Where(a => a.EntityType == nameof(Room) && a.EntityId == _roomId.ToString()).ExecuteDeleteAsync();
        await db.AuditLogs.Where(a => a.EntityType == "User" && a.EntityId == _secondUserId.ToString()).ExecuteDeleteAsync();
        await db.UserRoomScopes.Where(s => s.UserId == _secondUserId).ExecuteDeleteAsync();
        await db.UserRoles.Where(r => r.UserId == _secondUserId).ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == _secondUserId).ExecuteDeleteAsync();
        await db.Rooms.Where(r => r.Id == _roomId).ExecuteDeleteAsync();
        await db.Buildings.Where(b => b.Id == _buildingId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Set_user_roles_and_room_scope_persists_and_is_readable()
    {
        if (!_db) return;

        (await _client.PutAsJsonAsync($"/api/v1/users/{_secondUserId}/roles", new[] { SeededRoles.TechnicianId }))
            .EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync($"/api/v1/users/{_secondUserId}/room-scopes", new[] { _roomId }))
            .EnsureSuccessStatusCode();

        var detail = await _client.GetFromJsonAsync<UserDetailDto>($"/api/v1/users/{_secondUserId}");
        detail!.RoleIds.Should().ContainSingle().Which.Should().Be(SeededRoles.TechnicianId);
        detail.RoomScopeIds.Should().ContainSingle().Which.Should().Be(_roomId);

        var update = await _client.PutAsJsonAsync($"/api/v1/users/{_secondUserId}",
            new { fullName = "Second User Updated", phone = "0900000000", isActive = true });
        update.EnsureSuccessStatusCode();
        var updated = await _client.GetFromJsonAsync<UserDetailDto>($"/api/v1/users/{_secondUserId}");
        updated!.FullName.Should().Be("Second User Updated");
    }

    [Fact]
    public async Task Last_administrator_guard_allows_demotion_once_a_second_administrator_exists()
    {
        // This deliberately does NOT try to demote test-admin itself: this suite runs against a
        // real, shared dev database that already has other active Administrators (e.g. "mvt" from
        // earlier manual testing), so the guard's *blocking* path can't be exercised here without
        // either polluting that shared admin state or racing every other test's use of the
        // bootstrap admin bearer token (UserProvisioner also self-heals a bootstrap admin's role
        // back on their very next request, which would otherwise mask a real block/no-block bug).
        // The blocking path itself is a straightforward count query (LastAdministratorGuard) — a
        // clean/isolated database would be needed to integration-test it safely; see ADR-0010.
        if (!_db) return;

        (await _client.PutAsJsonAsync($"/api/v1/users/{_secondUserId}/roles", new[] { SeededRoles.AdministratorId }))
            .EnsureSuccessStatusCode();
        var promoted = await _client.GetFromJsonAsync<UserDetailDto>($"/api/v1/users/{_secondUserId}");
        promoted!.RoleIds.Should().Contain(SeededRoles.AdministratorId);

        // Now that a second active Administrator (second-user) exists alongside test-admin/mvt,
        // demoting second-user back down must succeed (not the last one).
        (await _client.PutAsJsonAsync($"/api/v1/users/{_secondUserId}/roles", Array.Empty<int>()))
            .EnsureSuccessStatusCode();
        var demoted = await _client.GetFromJsonAsync<UserDetailDto>($"/api/v1/users/{_secondUserId}");
        demoted!.RoleIds.Should().NotContain(SeededRoles.AdministratorId);
    }

    [Fact]
    public async Task Regenerate_room_code_rotates_it_and_is_audited()
    {
        if (!_db) return;

        var before = await _client.GetFromJsonAsync<RoomDetailDto>($"/api/v1/rooms/{_roomId}");

        var res = await _client.PostAsync($"/api/v1/rooms/{_roomId}/regenerate-code", null);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<CodeResult>();

        body!.Code.Should().NotBe(before!.Code);
        body.Code.Should().HaveLength(8);

        var after = await _client.GetFromJsonAsync<RoomDetailDto>($"/api/v1/rooms/{_roomId}");
        after!.Code.Should().Be(body.Code);

        var logs = await _client.GetFromJsonAsync<PagedResultDto<AuditLogDto>>(
            $"/api/v1/audit-logs?action=Room.RegenerateCode&pageSize=200");
        logs!.Items.Should().Contain(l => l.EntityId == _roomId.ToString());
    }

    private sealed record CodeResult(string Code);
    private sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
}
