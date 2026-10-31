using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LabMonitoring.Application.FileTransfers;
using LabMonitoring.Contracts.Agent;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.IntegrationTests.TestAuth;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabMonitoring.IntegrationTests;

/// <summary>File transfer: allowed folders, push (server → clients), collect (clients → server), and the rule that an
/// Agent may only touch transfers it is a target of. DB-gated, self-cleaning.</summary>
public sealed class FileTransferEndpointTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAdminClient();
    private bool _db;
    private int _buildingId, _roomId, _otherRoomId;

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        _db = await db.Database.CanConnectAsync();
        if (!_db) return;

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var building = new Building { Name = $"FT {suffix}", Code = $"FB-{suffix}" };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();
        _buildingId = building.Id;

        var room = new Room { BuildingId = _buildingId, Name = $"FT Lab {suffix}", Code = $"FR-{suffix}", Capacity = 10 };
        var other = new Room { BuildingId = _buildingId, Name = $"FT Other {suffix}", Code = $"FO-{suffix}", Capacity = 10 };
        db.Rooms.AddRange(room, other);
        await db.SaveChangesAsync();
        _roomId = room.Id;
        _otherRoomId = other.Id;
    }

    public async Task DisposeAsync()
    {
        if (!_db) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        var roomIds = new[] { _roomId, _otherRoomId };
        var computerIds = await db.Computers.Where(c => roomIds.Contains(c.RoomId)).Select(c => c.Id).ToListAsync();
        await db.FileTransfers.Where(t => (t.RoomId != null && roomIds.Contains(t.RoomId.Value))
                                          || (t.ComputerId != null && computerIds.Contains(t.ComputerId.Value))).ExecuteDeleteAsync();
        await db.CommandResults.Where(r => computerIds.Contains(r.ComputerId)).ExecuteDeleteAsync();
        await db.Commands.Where(c => (c.RoomId != null && roomIds.Contains(c.RoomId.Value))
                                     || (c.ComputerId != null && computerIds.Contains(c.ComputerId.Value))).ExecuteDeleteAsync();
        await db.Computers.Where(c => roomIds.Contains(c.RoomId)).ExecuteDeleteAsync();
        await db.TransferFolders.Where(f => f.RoomId != null && roomIds.Contains(f.RoomId.Value)).ExecuteDeleteAsync();
        await db.Rooms.Where(r => roomIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.Buildings.Where(b => b.Id == _buildingId).ExecuteDeleteAsync();
    }

    private async Task<(int computerId, string token)> RegisterApprovedAsync(int roomId, string hostname)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();
        var roomCode = await db.Rooms.Where(r => r.Id == roomId).Select(r => r.Code).SingleAsync();

        var reg = await factory.CreateClient().PostAsJsonAsync("/api/v1/agent/register",
            new { machineId = Guid.NewGuid(), roomCode, hostname });
        var body = (await reg.Content.ReadFromJsonAsync<AgentRegisterResponse>())!;
        (await _client.PostAsync($"/api/v1/computers/{body.ComputerId}/approve", null)).EnsureSuccessStatusCode();
        return (body.ComputerId, body.AgentToken);
    }

    private async Task<int> CreateFolderAsync(int? roomId, string direction, string path = @"D:\BaiTap")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/transfer-folders", new { roomId, path, direction, label = $"{direction} test", isActive = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<int>();
    }

    private static MultipartFormDataContent PushForm(string fileName, byte[] bytes, int folderId, int? roomId, int? computerId, bool confirm)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(folderId.ToString()), "FolderId" },
            { new StringContent(confirm ? "true" : "false"), "Confirm" },
        };
        if (roomId is { } r) form.Add(new StringContent(r.ToString()), "RoomId");
        if (computerId is { } c) form.Add(new StringContent(c.ToString()), "ComputerId");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "File", fileName);
        return form;
    }

    private HttpRequestMessage AgentGet(string url, string token)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, url);
        msg.Headers.Authorization = new AuthenticationHeaderValue("AgentToken", token);
        return msg;
    }

    [Fact]
    public async Task Room_wide_push_reaches_every_machine_and_only_targets_can_download_the_file()
    {
        if (!_db) return;
        var folderId = await CreateFolderAsync(_roomId, "Push");
        var (pcA, tokenA) = await RegisterApprovedAsync(_roomId, "ft-pc-a");
        var (pcB, _) = await RegisterApprovedAsync(_roomId, "ft-pc-b");
        var (_, outsiderToken) = await RegisterApprovedAsync(_otherRoomId, "ft-outsider");
        var content = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

        var response = await _client.PostAsync("/api/v1/file-transfers/push", PushForm("de-thi.pdf", content, folderId, _roomId, null, confirm: true));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<PushTransferResult>())!;
        result.TargetCount.Should().Be(2);

        var detail = (await _client.GetFromJsonAsync<FileTransferDetail>($"/api/v1/file-transfers/{result.TransferId}"))!;
        detail.Targets.Select(t => t.ComputerId).Should().BeEquivalentTo([pcA, pcB]);
        detail.Transfer.FileName.Should().Be("de-thi.pdf");

        // The target machine gets the exact bytes...
        var download = await factory.CreateClient().SendAsync(AgentGet($"/api/v1/agent/transfers/{result.TransferId}/file", tokenA));
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);

        // ...and can resume with a Range request.
        var ranged = AgentGet($"/api/v1/agent/transfers/{result.TransferId}/file", tokenA);
        ranged.Headers.Range = new RangeHeaderValue(1000, null);
        var partial = await factory.CreateClient().SendAsync(ranged);
        partial.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await partial.Content.ReadAsByteArrayAsync()).Should().Equal(content.Skip(1000));

        // A machine in another room is not a target: forbidden.
        var outsider = await factory.CreateClient().SendAsync(AgentGet($"/api/v1/agent/transfers/{result.TransferId}/file", outsiderToken));
        outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Push_is_rejected_for_bad_requests()
    {
        if (!_db) return;
        var pushFolder = await CreateFolderAsync(_roomId, "Push");
        var collectFolder = await CreateFolderAsync(_roomId, "Collect", @"D:\BaiNop");
        var otherRoomFolder = await CreateFolderAsync(_otherRoomId, "Push", @"E:\Khac");
        var (pc, _) = await RegisterApprovedAsync(_roomId, "ft-pc-bad");
        var bytes = new byte[] { 1, 2, 3 };

        // room-wide without confirm
        (await _client.PostAsync("/api/v1/file-transfers/push", PushForm("a.txt", bytes, pushFolder, _roomId, null, confirm: false)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // executable extension
        (await _client.PostAsync("/api/v1/file-transfers/push", PushForm("setup.exe", bytes, pushFolder, null, pc, confirm: false)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // alternate data stream / reserved device name
        (await _client.PostAsync("/api/v1/file-transfers/push", PushForm("a.txt:evil", bytes, pushFolder, null, pc, confirm: false)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PostAsync("/api/v1/file-transfers/push", PushForm("CON.txt", bytes, pushFolder, null, pc, confirm: false)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // a Collect folder cannot be used for push, nor another room's folder
        (await _client.PostAsync("/api/v1/file-transfers/push", PushForm("a.txt", bytes, collectFolder, null, pc, confirm: false)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsync("/api/v1/file-transfers/push", PushForm("a.txt", bytes, otherRoomFolder, null, pc, confirm: false)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Collect_uploads_are_stored_per_machine_and_downloadable_by_the_admin()
    {
        if (!_db) return;
        var folderId = await CreateFolderAsync(_roomId, "Collect", @"D:\BaiNop");
        var (pcA, tokenA) = await RegisterApprovedAsync(_roomId, "ft-collect-a");
        var (_, tokenOut) = await RegisterApprovedAsync(_otherRoomId, "ft-collect-out");

        var issue = await _client.PostAsJsonAsync("/api/v1/file-transfers/collect",
            new { computerId = pcA, roomId = (int?)null, folderId, filter = "*.docx;*.pdf", confirm = false });
        issue.StatusCode.Should().Be(HttpStatusCode.OK, await issue.Content.ReadAsStringAsync());
        var transferId = (await issue.Content.ReadFromJsonAsync<PushTransferResult>())!.TransferId;

        var zip = new byte[] { 0x50, 0x4B, 3, 4, 9, 9, 9 };
        HttpRequestMessage Upload(string token) => new(HttpMethod.Post, $"/api/v1/agent/transfers/{transferId}/collected?fileCount=3&truncated=false")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("AgentToken", token) },
            Content = new ByteArrayContent(zip),
        };

        (await factory.CreateClient().SendAsync(Upload(tokenOut))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().SendAsync(Upload(tokenA))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = (await _client.GetFromJsonAsync<FileTransferDetail>($"/api/v1/file-transfers/{transferId}"))!;
        var target = detail.Targets.Should().ContainSingle().Subject;
        target.CollectedFileCount.Should().Be(3);
        target.CollectedTruncated.Should().BeFalse();
        target.CollectedBytes.Should().Be(zip.Length);

        var download = await _client.GetAsync($"/api/v1/file-transfers/{transferId}/collected/{pcA}");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(zip);
        download.Content.Headers.ContentDisposition!.FileNameStar.Should().EndWith(".zip");
    }

    [Fact]
    public async Task Agent_config_lists_the_active_folders_for_its_room_only()
    {
        if (!_db) return;
        var mine = await CreateFolderAsync(_roomId, "Push", @"D:\Mine");
        await CreateFolderAsync(_otherRoomId, "Push", @"D:\NotMine");
        var (_, token) = await RegisterApprovedAsync(_roomId, "ft-config");

        var response = await factory.CreateClient().SendAsync(AgentGet("/api/v1/agent/config", token));
        var config = (await response.Content.ReadFromJsonAsync<AgentConfigResponse>())!;

        config.TransferFolders.Should().Contain(f => f.Id == mine && f.Path == @"D:\Mine" && f.Direction == "Push");
        config.TransferFolders.Should().NotContain(f => f.Path == @"D:\NotMine");
    }

    [Theory]
    [InlineData(@"relative\path")]
    [InlineData(@"\\server\share")]
    [InlineData(@"D:\..\Windows")]
    public async Task Folder_paths_must_be_absolute_local_paths(string path)
    {
        if (!_db) return;
        var response = await _client.PostAsJsonAsync("/api/v1/transfer-folders", new { roomId = _roomId, path, direction = "Push", label = "bad", isActive = true });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
