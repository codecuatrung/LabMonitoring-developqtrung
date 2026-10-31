using LabMonitoring.Agent.RemoteHelper;

// This process runs headless — no console is attached (launched via CreateProcessAsUser with
// CREATE_NO_WINDOW) — so Console output goes nowhere. HelperLog is the only way to see what
// happened here; log everything, including things that would otherwise silently kill the process.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    HelperLog.Write($"UNHANDLED EXCEPTION (terminating={e.IsTerminating}): {e.ExceptionObject}");
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    HelperLog.Write($"UNOBSERVED TASK EXCEPTION: {e.Exception}");
    e.SetObserved();
};

HelperLog.Write($"Starting. Args: [{string.Join(' ', args)}]");

if (!OperatingSystem.IsWindows())
{
    HelperLog.Write("Not running on Windows — exiting.");
    return 1;
}

// "--mode present" (screen presentation + input lock) shares this exe with remote control so
// publish.ps1 and the auto-updater keep shipping a single helper.
if (args.Contains("--mode") && args[Array.IndexOf(args, "--mode") + 1] == "present")
{
    var presentPipe = args[Array.IndexOf(args, "--pipe") + 1];
    var lockInput = args.Contains("--lock");
    try
    {
        new PresentationHelper(presentPipe, lockInput).Run();
        return 0;
    }
    catch (Exception ex)
    {
        HelperLog.Write($"Presentation failed: {ex}");
        return 1;
    }
}

Guid? sessionId = null;
string? pipeName = null;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--session" && Guid.TryParse(args[i + 1], out var parsed))
        sessionId = parsed;
    else if (args[i] == "--pipe")
        pipeName = args[i + 1];
}

if (sessionId is null || string.IsNullOrEmpty(pipeName))
{
    HelperLog.Write("Usage: LabMonitoring.Agent.RemoteHelper --session <guid> --pipe <name> — missing/invalid args, exiting.");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await RemoteControlSession.RunAsync(sessionId.Value, pipeName, cts.Token);
    HelperLog.Write($"Session {sessionId} finished normally.");
    return 0;
}
catch (Exception ex)
{
    HelperLog.Write($"Session {sessionId} failed: {ex}");
    return 1;
}
