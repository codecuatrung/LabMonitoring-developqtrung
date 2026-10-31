using System.Text.Json;
using LabMonitoring.Agent.Updater;

// Standalone helper spawned by AgentUpdateExecutor (SRS H1 "auto-update") to do the one thing the
// Agent Windows Service can't do to itself while running: stop, swap its own install directory for a
// newly staged version, and restart. Runs as LocalSystem (inherited from the service that spawned
// it), detached, no console — UpdaterLog is the only way to see what happened here.

var parsedArgs = ParseArgs(args);
UpdaterLog.Write($"Starting. Args: [{string.Join(' ', args)}]");

string serviceName = parsedArgs["service"];
string stagedDir = parsedArgs["staged"];
string installDir = parsedArgs["install"];
string backupDir = parsedArgs["backup"];
string resultFile = parsedArgs["result-file"];
long commandId = long.Parse(parsedArgs["command-id"]);
string newVersion = parsedArgs["new-version"];

// Phase A — not committed yet. If we can't even stop the service, leave everything untouched and
// exit without writing a result: the still-running old Agent process is unaffected, and the server's
// CommandTimeoutService will eventually mark this command Timeout on its own (SRS FR-4.5).
if (!ServiceControl.Stop(serviceName, TimeSpan.FromSeconds(30)))
{
    UpdaterLog.Write($"Could not stop service '{serviceName}' within timeout — aborting without touching the install directory.");
    return 1;
}

// Phase B — committed. Every path from here ends with the service running again (new version or
// rolled-back old version) and a result file written, so whichever process starts next reports it.
try
{
    if (Directory.Exists(backupDir))
    {
        UpdaterLog.Write($"Removing stale backup '{backupDir}' from a previous attempt.");
        Directory.Delete(backupDir, recursive: true);
    }

    DirectoryOps.Move(installDir, backupDir);
    DirectoryOps.Move(stagedDir, installDir);
    UpdaterLog.Write($"Swapped install directory: '{installDir}' is now version {newVersion} (previous backed up at '{backupDir}').");
}
catch (Exception ex)
{
    UpdaterLog.Write($"Swap failed: {ex}");
    TryRestore(installDir, backupDir);
    ServiceControl.Start(serviceName, TimeSpan.FromSeconds(30));
    WriteResult(resultFile, commandId, "Failed", $"Failed to swap install directory: {ex.Message}");
    return 1;
}

if (ServiceControl.Start(serviceName, TimeSpan.FromSeconds(30)) && ServiceControl.StaysUpFor(serviceName, TimeSpan.FromSeconds(15)))
{
    UpdaterLog.Write($"Version {newVersion} started and stayed up — update succeeded.");
    WriteResult(resultFile, commandId, "Executed", null);
    return 0;
}

// New version failed its health check — roll back to the backed-up previous version.
UpdaterLog.Write($"Version {newVersion} failed to start or crashed shortly after — rolling back.");
try
{
    ServiceControl.Stop(serviceName, TimeSpan.FromSeconds(15)); // best-effort; it may already be down
    var parentDir = Path.GetDirectoryName(installDir)!;
    var failedDir = Path.Combine(parentDir, $"Agent_failed_{newVersion}");
    if (Directory.Exists(failedDir))
        Directory.Delete(failedDir, recursive: true);
    if (Directory.Exists(installDir))
        DirectoryOps.Move(installDir, failedDir); // keep the bad build around for diagnosis
    DirectoryOps.Move(backupDir, installDir);

    var restarted = ServiceControl.Start(serviceName, TimeSpan.FromSeconds(30));
    UpdaterLog.Write(restarted
        ? $"Rolled back to previous version successfully (bad build kept at '{failedDir}')."
        : "Rolled back files but the previous version did not restart either.");

    WriteResult(resultFile, commandId, "Failed", $"Version {newVersion} failed to start; rolled back to the previous version.");
}
catch (Exception ex)
{
    UpdaterLog.Write($"Rollback failed: {ex}");
    WriteResult(resultFile, commandId, "Failed", $"Version {newVersion} failed AND rollback failed: {ex.Message}");
}

return 1;

static void TryRestore(string installDir, string backupDir)
{
    try
    {
        if (!Directory.Exists(installDir) && Directory.Exists(backupDir))
            DirectoryOps.Move(backupDir, installDir);
    }
    catch (Exception ex)
    {
        UpdaterLog.Write($"TryRestore failed: {ex}");
    }
}

static void WriteResult(string path, long commandId, string status, string? errorMessage)
{
    try
    {
        // AgentWorker.ReportPendingUpdateResultAsync reads this with JsonSerializerDefaults.Web too —
        // match casing for symmetry (PropertyNameCaseInsensitive on the reading side would forgive a
        // mismatch anyway, but there's no reason to rely on that).
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(new { CommandId = commandId, Status = status, ErrorMessage = errorMessage }, jsonOpts);
        File.WriteAllText(path, json);
    }
    catch (Exception ex)
    {
        UpdaterLog.Write($"Failed to write result file '{path}': {ex}");
    }
}

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var result = new Dictionary<string, string>();
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i].StartsWith("--", StringComparison.Ordinal))
            result[argv[i][2..]] = argv[i + 1];
    }
    return result;
}
