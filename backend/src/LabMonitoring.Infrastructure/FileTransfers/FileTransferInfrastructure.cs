using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace LabMonitoring.Infrastructure.FileTransfers;

/// <summary>Binds "FileTransfer": size limits, blocked extensions, download concurrency, retention.</summary>
public sealed class FileTransferOptions
{
    public const string SectionName = "FileTransfer";

    public long MaxPushBytes { get; init; } = 500L * 1024 * 1024;
    public long MaxCollectBytes { get; init; } = 200L * 1024 * 1024;

    public string[] BlockedExtensionList { get; init; } =
        [".exe", ".dll", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".vbs", ".js", ".com", ".jar", ".lnk"];

    /// <summary>How many Agents may download pushed files at the same time (others get 429 and retry).</summary>
    public int MaxConcurrentDownloads { get; init; } = 8;

    /// <summary>Transfers (and their stored files) older than this are deleted.</summary>
    public int RetentionDays { get; init; } = 14;

}

internal sealed class FileTransferPolicy(IOptions<FileTransferOptions> options) : IFileTransferPolicy
{
    private readonly FileTransferOptions _o = options.Value;
    public long MaxPushBytes => _o.MaxPushBytes;
    public long MaxCollectBytes => _o.MaxCollectBytes;
    public IReadOnlyCollection<string> BlockedExtensions => _o.BlockedExtensionList;
}

/// <summary>Files under <see cref="ApiPaths.FileTransfersDirectory"/>: <c>{transferId}/push.bin</c> and
/// <c>{transferId}/collected-{computerId}.zip</c>. Ids are ints, so no caller-supplied path is ever used.</summary>
internal sealed class LocalDiskFileTransferStore : IFileTransferStore
{
    private static string TransferDir(int transferId)
    {
        var dir = Path.Combine(ApiPaths.FileTransfersDirectory, transferId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public Task<(string Sha256, long SizeBytes)> SavePushFileAsync(int transferId, Stream content, long maxBytes, CancellationToken ct = default)
        => SaveAsync(Path.Combine(TransferDir(transferId), "push.bin"), content, maxBytes, ct);

    public Task<(string Sha256, long SizeBytes)> SaveCollectedAsync(int transferId, int computerId, Stream content, long maxBytes, CancellationToken ct = default)
        => SaveAsync(Path.Combine(TransferDir(transferId), $"collected-{computerId}.zip"), content, maxBytes, ct);

    public Stream OpenPushFile(int transferId)
        => new FileStream(Path.Combine(TransferDir(transferId), "push.bin"), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public Stream? OpenCollected(int transferId, int computerId)
    {
        var path = Path.Combine(TransferDir(transferId), $"collected-{computerId}.zip");
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public void DeleteTransfer(int transferId)
    {
        var dir = Path.Combine(ApiPaths.FileTransfersDirectory, transferId.ToString());
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* best effort; the retention job retries */ }
    }

    public long AvailableFreeBytes()
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(ApiPaths.FileTransfersDirectory))!).AvailableFreeSpace; }
        catch { return long.MaxValue; }
    }

    private static async Task<(string, long)> SaveAsync(string path, Stream content, long maxBytes, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        long total = 0;
        try
        {
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                        throw new ArgumentException($"The file exceeds the {maxBytes / (1024 * 1024)} MB limit.");
                    sha256.TransformBlock(buffer, 0, read, null, 0);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            sha256.TransformFinalBlock([], 0, 0);
        }
        catch
        {
            try { File.Delete(path); } catch { /* best effort */ }
            throw;
        }

        return (Convert.ToHexStringLower(sha256.Hash!), total);
    }
}

internal sealed class TransferThrottle(IOptions<FileTransferOptions> options) : ITransferThrottle
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.MaxConcurrentDownloads));

    public IDisposable? TryAcquire() => _slots.Wait(0) ? new Lease(_slots) : null;

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                slots.Release();
        }
    }
}

/// <summary>Deletes transfers (rows + stored files) older than <see cref="FileTransferOptions.RetentionDays"/>
/// and warns when the volume is nearly full.</summary>
public sealed class FileTransferRetentionService(
    IServiceScopeFactory scopeFactory, IFileTransferStore store, IOptions<FileTransferOptions> options,
    ILogger<FileTransferRetentionService> logger) : BackgroundService
{
    private const long LowDiskBytes = 5L * 1024 * 1024 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "File transfer retention sweep failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, options.Value.RetentionDays));
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabMonitoringDbContext>();

        var old = await db.FileTransfers.Where(t => t.CreatedAt < cutoff).Select(t => t.Id).ToListAsync(ct);
        foreach (var id in old)
            store.DeleteTransfer(id);
        if (old.Count > 0)
        {
            await db.FileTransfers.Where(t => old.Contains(t.Id)).ExecuteDeleteAsync(ct);
            logger.LogInformation("Removed {Count} file transfer(s) older than {Cutoff:u}.", old.Count, cutoff);
        }

        var free = store.AvailableFreeBytes();
        if (free < LowDiskBytes)
            logger.LogWarning("File transfer storage is low on disk space: {FreeMb} MB free.", free / (1024 * 1024));
    }
}
