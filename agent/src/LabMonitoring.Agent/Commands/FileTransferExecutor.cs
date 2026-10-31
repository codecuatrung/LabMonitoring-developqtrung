using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LabMonitoring.Agent.Client;
using LabMonitoring.Agent.Configuration;
using LabMonitoring.Agent.Protocol;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Commands;

/// <summary>
/// <c>PushFile</c> (server → this machine) and <c>CollectFiles</c> (this machine → server). Every folder is validated by
/// <see cref="TransferPathGuard"/> against the synced allow-list before anything is read or written; one transfer runs
/// at a time per machine. Runs off the SignalR handler thread (see <c>AgentHubClient</c>) because it can take minutes.
/// </summary>
public sealed class FileTransferExecutor(
    TransferApiClient api, AgentConfigCache config, AgentPaths paths, ILogger<FileTransferExecutor> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const int MaxFilesPerCollect = 20_000;
    private const int MaxDownloadAttempts = 8;
    private const int MaxThrottleWaits = 60;
    private readonly SemaphoreSlim _busy = new(1, 1);

    public async Task<CommandExecutionResult> ExecuteAsync(CommandType type, string payloadJson, CancellationToken ct)
    {
        if (!_busy.Wait(0))
            return CommandExecutionResult.Fail("Another file transfer is already running on this machine.");
        try
        {
            return type == CommandType.PushFile
                ? await PushAsync(payloadJson, ct)
                : await CollectAsync(payloadJson, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "File transfer ({Type}) failed", type);
            return CommandExecutionResult.Fail(ex.Message);
        }
        finally
        {
            _busy.Release();
        }
    }

    // ---- Push ------------------------------------------------------------------------------------

    private async Task<CommandExecutionResult> PushAsync(string payloadJson, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PushFilePayload>(payloadJson, JsonOpts);
        if (p is null || p.TransferId <= 0 || string.IsNullOrWhiteSpace(p.Sha256) || p.SizeBytes <= 0)
            return CommandExecutionResult.Fail("Malformed PushFile payload.");

        if (!TransferPathGuard.TryResolveFolder(p.FolderId, p.FolderPath, "Push", config.Current, out var dir, out var error))
            return CommandExecutionResult.Fail(error);
        if (!TransferPathGuard.IsSafeFileName(p.FileName, out error))
            return CommandExecutionResult.Fail(error);

        Directory.CreateDirectory(dir);
        var finalPath = Path.Combine(dir, p.FileName);
        if (!TransferPathGuard.IsInside(dir, finalPath))
            return CommandExecutionResult.Fail("The file name resolves outside the allowed folder.");
        if (!p.Overwrite && File.Exists(finalPath))
            finalPath = UniquePath(dir, p.FileName);

        var free = new DriveInfo(Path.GetPathRoot(dir)!).AvailableFreeSpace;
        if (free < p.SizeBytes + 64L * 1024 * 1024)
            return CommandExecutionResult.Fail($"Not enough free disk space on the target drive ({free / (1024 * 1024)} MB free).");

        // Spread a whole room's downloads over a few seconds instead of all hitting the server in the same instant.
        if (p.SizeBytes > 10L * 1024 * 1024)
            await Task.Delay(Random.Shared.Next(0, 15_000), ct);

        var part = Path.Combine(dir, $".{p.TransferId}.{Path.GetFileName(finalPath)}.part");
        try
        {
            await DownloadAsync(p, part, ct);

            var actual = await Sha256Async(part, ct);
            if (!string.Equals(actual, p.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                return CommandExecutionResult.Fail("Checksum mismatch after download (file discarded).");
            }

            File.Move(part, finalPath, overwrite: p.Overwrite);
            logger.LogInformation("PushFile {TransferId}: saved {Path} ({Size} bytes)", p.TransferId, finalPath, p.SizeBytes);
            return CommandExecutionResult.Ok();
        }
        catch
        {
            // Keep a partial .part only for cancellation-style interruptions (resume next time is not
            // supported across commands, so remove it to avoid leaving clutter in a student-visible folder).
            try { if (File.Exists(part)) File.Delete(part); } catch { /* best effort */ }
            throw;
        }
    }

    private async Task DownloadAsync(PushFilePayload p, string part, CancellationToken ct)
    {
        var attempts = 0;
        var throttled = 0;
        var notReady = 0;

        while (true)
        {
            var have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > p.SizeBytes)
            {
                File.Delete(part);
                have = 0;
            }
            if (have == p.SizeBytes)
                return;

            try
            {
                using var response = await api.DownloadFileAsync(p.TransferId, have, ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (++throttled > MaxThrottleWaits)
                        throw new IOException("The server stayed busy; giving up on this download.");
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                    await Task.Delay(wait + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 5000)), ct);
                    continue;
                }
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    // Right after the command goes out the server may not have linked it to the transfer yet.
                    if (++notReady > 4)
                        throw new IOException($"The server refused the download ({(int)response.StatusCode}).");
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    continue;
                }
                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
                    throw new IOException($"Download failed with HTTP {(int)response.StatusCode}.");

                // 200 to a ranged request means the server sent the whole file again: start over.
                var append = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                await using var target = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                await body.CopyToAsync(target, 81920, ct);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException && ++attempts < MaxDownloadAttempts)
            {
                logger.LogWarning(ex, "PushFile {TransferId}: download interrupted (attempt {Attempt}); resuming.", p.TransferId, attempts);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    // ---- Collect ---------------------------------------------------------------------------------

    private async Task<CommandExecutionResult> CollectAsync(string payloadJson, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<CollectFilesPayload>(payloadJson, JsonOpts);
        if (p is null || p.TransferId <= 0 || p.MaxBytes <= 0)
            return CommandExecutionResult.Fail("Malformed CollectFiles payload.");

        if (!TransferPathGuard.TryResolveFolder(p.FolderId, p.FolderPath, "Collect", config.Current, out var root, out var error))
            return CommandExecutionResult.Fail(error);
        if (!Directory.Exists(root))
            return CommandExecutionResult.Fail("The source folder does not exist on this machine.");

        var workDir = Path.Combine(paths.DataDirectory, "transfers");
        Directory.CreateDirectory(workDir);
        var zipPath = Path.Combine(workDir, $"collect-{p.TransferId}.zip");

        try
        {
            var (count, truncated) = BuildZip(root, zipPath, p.Filter, p.MaxBytes);
            if (count == 0)
                return CommandExecutionResult.Fail("No matching files were found in the folder.");

            HttpStatusCode status = default;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await using var zip = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                try
                {
                    status = await api.UploadCollectedAsync(p.TransferId, zip, count, truncated, ct);
                    if (status == HttpStatusCode.NoContent || status == HttpStatusCode.OK)
                        break;
                    if (status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound && attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), ct);
                        continue;
                    }
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException && attempt < 3)
                {
                    logger.LogWarning(ex, "CollectFiles {TransferId}: upload failed (attempt {Attempt}).", p.TransferId, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }

            if (status is not (HttpStatusCode.NoContent or HttpStatusCode.OK))
                return CommandExecutionResult.Fail($"The server rejected the upload (HTTP {(int)status}).");

            logger.LogInformation("CollectFiles {TransferId}: uploaded {Count} file(s) from {Root}{Trunc}",
                p.TransferId, count, root, truncated ? " (truncated at the size cap)" : string.Empty);
            return CommandExecutionResult.Ok();
        }
        finally
        {
            try { File.Delete(zipPath); } catch { /* best effort */ }
        }
    }

    /// <summary>Zips files matching the filter (recursive, skipping junctions and files that can't be read) into
    /// <paramref name="zipPath"/>, stopping at <paramref name="maxBytes"/>. Never modifies the source.</summary>
    public static (int Count, bool Truncated) BuildZip(string root, string zipPath, string? filter, long maxBytes)
    {
        var patterns = (filter ?? string.Empty)
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .DefaultIfEmpty("*")
            .ToArray();

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };

        var files = patterns
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, options))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var count = 0;
        var truncated = false;
        using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                if (count >= MaxFilesPerCollect)
                {
                    truncated = true;
                    break;
                }

                long length;
                try { length = new FileInfo(file).Length; }
                catch (IOException) { continue; }

                // Worst case (incompressible) — keeps the finished zip within the cap.
                if (fs.Length + length > maxBytes)
                {
                    truncated = true;
                    continue;
                }

                ZipArchiveEntry? entry = null;
                try
                {
                    // ReadWrite|Delete sharing: read files that another program (Word, ...) still has open.
                    using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    entry = zip.CreateEntry(Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Fastest);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using var target = entry.Open();
                    source.CopyTo(target);
                    count++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    try { entry?.Delete(); } catch { /* entry may be half-written; the zip is finalized anyway */ }
                }
            }
        }

        return (count, truncated);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static string UniquePath(string dir, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException("Could not find a free file name.");
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }
}
