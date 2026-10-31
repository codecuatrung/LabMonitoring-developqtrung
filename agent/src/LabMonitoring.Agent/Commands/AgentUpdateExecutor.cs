using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LabMonitoring.Agent.Client;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Commands;

/// <summary>
/// Handles <c>CommandType.UpdateAgent</c> (SRS H1 "auto-update"): downloads the package the server
/// says is current, verifies its SHA-256 against the value carried inside the HMAC-signed command
/// payload (<see cref="CommandReceiver"/> already verified the envelope, so this hash is
/// tamper-evident without a separate file signature), stages it, then hands off to a small standalone
/// <c>LabMonitoring.Agent.Updater</c> process that stops this Windows Service, swaps the install
/// directory, and restarts it — work this process can't do to itself while it's still running.
/// </summary>
public sealed class AgentUpdateExecutor(AgentApiClient api, AgentPaths paths, ILogger<AgentUpdateExecutor> logger)
{
    private const string ServiceName = "LabMonitoringAgent";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<CommandExecutionResult> ExecuteAsync(long commandId, string payloadJson, CancellationToken ct)
    {
        UpdatePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UpdatePayload>(payloadJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            return CommandExecutionResult.Fail($"Malformed UpdateAgent payload: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(payload?.Version) || string.IsNullOrWhiteSpace(payload.Sha256))
            return CommandExecutionResult.Fail("Malformed UpdateAgent payload.");

        if (string.Equals(payload.Version, AgentRegistrationService.AgentVersion, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("UpdateAgent {CommandId}: already at version {Version}, nothing to do.", commandId, payload.Version);
            return CommandExecutionResult.Ok();
        }

        try
        {
            var stagedDir = await DownloadAndStageAsync(commandId, payload, ct);
            LaunchUpdater(commandId, payload.Version, stagedDir);
            return CommandExecutionResult.DeferredResult();
        }
        catch (ChecksumMismatchException ex)
        {
            logger.LogWarning("UpdateAgent {CommandId}: {Message}", commandId, ex.Message);
            return CommandExecutionResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateAgent {CommandId} failed", commandId);
            return CommandExecutionResult.Fail(ex.Message);
        }
    }

    private async Task<string> DownloadAndStageAsync(long commandId, UpdatePayload payload, CancellationToken ct)
    {
        var workDir = Path.Combine(paths.DataDirectory, "update-work");
        Directory.CreateDirectory(workDir);
        var zipPath = Path.Combine(workDir, $"{payload.Version}.zip");

        using var response = await api.DownloadUpdatePackageAsync(ct)
            ?? throw new InvalidOperationException("Server has no Agent package available to download.");

        await using (var fileStream = File.Create(zipPath))
        await using (var remoteStream = await response.Content.ReadAsStreamAsync(ct))
        {
            await remoteStream.CopyToAsync(fileStream, ct);
        }

        string actualHash;
        await using (var toHash = File.OpenRead(zipPath))
            actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(toHash, ct));

        if (!string.Equals(actualHash, payload.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zipPath);
            throw new ChecksumMismatchException(
                $"Downloaded package checksum mismatch for version {payload.Version} (expected {payload.Sha256}, got {actualHash}).");
        }

        var stagedDir = Path.Combine(workDir, "staged", payload.Version!);
        if (Directory.Exists(stagedDir))
            Directory.Delete(stagedDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, stagedDir);
        File.Delete(zipPath);

        logger.LogInformation("UpdateAgent {CommandId}: staged version {Version} at {StagedDir}.", commandId, payload.Version, stagedDir);
        return stagedDir;
    }

    private void LaunchUpdater(long commandId, string newVersion, string stagedDir)
    {
        // The install directory is wherever this running exe lives — AppContext.BaseDirectory, not a
        // config value, so it's always correct regardless of where install-service.ps1 put it.
        var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var bundledUpdaterDir = Path.Combine(installDir, "Updater");
        if (!File.Exists(Path.Combine(bundledUpdaterDir, "LabMonitoring.Agent.Updater.exe")))
            throw new FileNotFoundException($"Updater executable not found under '{bundledUpdaterDir}'.");

        // Copy the Updater OUT of the install directory before running it — installDir itself gets
        // renamed to backupDir mid-flight, which would pull the rug out from under the very process
        // whose files live there. %ProgramData% (paths.DataDirectory) is untouched by the swap.
        var runtimeDir = Path.Combine(paths.DataDirectory, "updater-runtime");
        if (Directory.Exists(runtimeDir))
            Directory.Delete(runtimeDir, recursive: true);
        CopyDirectory(bundledUpdaterDir, runtimeDir);
        var updaterExe = Path.Combine(runtimeDir, "LabMonitoring.Agent.Updater.exe");

        var parentDir = Path.GetDirectoryName(installDir)
            ?? throw new InvalidOperationException($"Install directory '{installDir}' has no parent.");
        var backupDir = Path.Combine(parentDir, $"Agent_backup_{AgentRegistrationService.AgentVersion}");
        var resultFile = Path.Combine(paths.DataDirectory, "pending-command-result.json");

        var args = string.Join(' ',
            "--service", Quote(ServiceName),
            "--staged", Quote(stagedDir),
            "--install", Quote(installDir),
            "--backup", Quote(backupDir),
            "--result-file", Quote(resultFile),
            "--command-id", commandId.ToString(),
            "--new-version", Quote(newVersion));

        var psi = new ProcessStartInfo(updaterExe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = runtimeDir,
        };
        Process.Start(psi);
        logger.LogInformation("UpdateAgent {CommandId}: launched Updater ({UpdaterExe}) for version {Version}.", commandId, updaterExe, newVersion);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string Quote(string value) => $"\"{value}\"";

    private sealed record UpdatePayload(string? Version, string? Sha256);

    private sealed class ChecksumMismatchException(string message) : Exception(message);
}
