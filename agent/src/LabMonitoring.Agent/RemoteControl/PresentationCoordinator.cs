using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using LabMonitoring.Agent.Client;
using LabMonitoring.Agent.Commands;
using LabMonitoring.Agent.Monitoring;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.RemoteControl;

/// <summary>
/// Owns the lifecycle of a screen presentation on this machine: launches the RemoteHelper in
/// "present" mode into the interactive session (full-screen overlay + optional input lock), joins the
/// server's frame group, and pipes each incoming JPEG frame to the helper. Only ever active for a
/// session announced by a signed <c>StartPresentation</c> command. Safety nets so a machine is never
/// left locked: an explicit Stop command, the server's <c>PresentationEnded</c> event, the frame
/// dead-man timer (the browser sends a keepalive frame every ~2s), and the helper's own silence limit.
/// If a student kills the helper while the session is still valid, it is relaunched.
/// </summary>
public sealed class PresentationCoordinator : IDisposable
{
    private const byte MsgFrame = 1;
    private const byte MsgStop = 2;
    private static readonly TimeSpan FrameDeadMan = TimeSpan.FromSeconds(30);

    private readonly AgentHubClient _hub;
    private readonly SessionInfoProvider _sessionInfo;
    private readonly ILogger<PresentationCoordinator> _logger;
    private readonly object _lock = new();

    private Active? _active;

    public PresentationCoordinator(AgentHubClient hub, SessionInfoProvider sessionInfo, ILogger<PresentationCoordinator> logger)
    {
        _hub = hub;
        _sessionInfo = sessionInfo;
        _logger = logger;

        _hub.PresentationFrameReceived += OnFrame;
        _hub.PresentationEnded += sessionId => Stop(sessionId, "ended by server");
        _hub.HubReconnected += () => _ = RejoinAsync();
    }

    public async Task<CommandExecutionResult> StartAsync(Guid sessionId, bool lockInput)
    {
        if (!OperatingSystem.IsWindows())
            return CommandExecutionResult.Fail("Presentation is only supported on Windows.");
        if (string.IsNullOrWhiteSpace(_sessionInfo.CurrentConsoleUser()))
            return CommandExecutionResult.Fail("No interactive user is logged in on the console.");

        Active active;
        lock (_lock)
        {
            if (_active?.SessionId == sessionId)
                return CommandExecutionResult.Ok();
            _active?.Cts.Cancel(); // a newer session replaces a stale one
            active = _active = new Active(sessionId, lockInput);
        }

        try
        {
            await _hub.JoinPresentationAsync(sessionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presentation {SessionId}: could not join the frame group.", sessionId);
            Stop(sessionId, "join failed");
            return CommandExecutionResult.Fail($"Could not join presentation: {ex.Message}");
        }

        _ = RunAsync(active);
        _logger.LogInformation("Presentation {SessionId} started (lockInput={Lock}).", sessionId, lockInput);
        return CommandExecutionResult.Ok();
    }

    public CommandExecutionResult Stop(Guid sessionId, string reason)
    {
        Active? active;
        lock (_lock)
        {
            active = _active;
            if (active is null || active.SessionId != sessionId)
                return CommandExecutionResult.Ok(); // nothing to stop — idempotent
            _active = null;
        }

        _logger.LogInformation("Presentation {SessionId} stopping ({Reason}).", sessionId, reason);
        active.Cts.Cancel();
        return CommandExecutionResult.Ok();
    }

    private void OnFrame(Guid sessionId, byte[] jpeg)
    {
        Active? active;
        lock (_lock) { active = _active; }
        if (active is null || active.SessionId != sessionId)
            return;

        active.LatestFrame = jpeg;
        active.LastFrameUtc = DateTime.UtcNow;
        active.FrameSignal.Release();
    }

    private async Task RejoinAsync()
    {
        Active? active;
        lock (_lock) { active = _active; }
        if (active is null)
            return;
        try { await _hub.JoinPresentationAsync(active.SessionId, CancellationToken.None); }
        catch (Exception ex) { _logger.LogWarning(ex, "Presentation {SessionId}: rejoin after reconnect failed.", active.SessionId); }
    }

    [SupportedOSPlatform("windows")]
    private async Task RunAsync(Active active)
    {
        var ct = active.Cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await RunHelperOnceAsync(active, ct);
                if (!ct.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromSeconds(1), ct); // helper died (killed?) — relaunch
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Presentation {SessionId}: run loop failed.", active.SessionId);
        }
        finally
        {
            lock (_lock)
            {
                if (_active == active)
                    _active = null;
            }
            active.Cts.Dispose();
            _logger.LogInformation("Presentation {SessionId} ended.", active.SessionId);
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task RunHelperOnceAsync(Active active, CancellationToken ct)
    {
        var pipeName = $"LabMonitoringAgent.Presentation.{active.SessionId:N}.{Guid.NewGuid():N}";
        await using var pipe = CreatePipeServer(pipeName);

        var helperExe = Path.Combine(AppContext.BaseDirectory, "RemoteHelper", "LabMonitoring.Agent.RemoteHelper.exe");
        var arguments = $"--mode present --pipe {pipeName}{(active.LockInput ? " --lock" : "")}";
        var user = _sessionInfo.CurrentConsoleUser();

        if (!TaskSchedulerProcessLauncher.TryLaunch(helperExe, arguments, user, _logger, out var process, out var error))
        {
            _logger.LogError("Presentation {SessionId}: failed to launch helper: {Error}", active.SessionId, error);
            return;
        }

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(20));
            try { await pipe.WaitForConnectionAsync(connectCts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Presentation {SessionId}: helper never connected to the pipe.", active.SessionId);
                return;
            }

            _logger.LogInformation("Presentation {SessionId}: helper connected (pid {Pid}).", active.SessionId, process?.Id);
            active.LastFrameUtc = DateTime.UtcNow;
            if (active.LatestFrame is not null)
                active.FrameSignal.Release(); // paint the current slide on a freshly (re)started helper

            try
            {
            while (!ct.IsCancellationRequested)
            {
                var signalled = await active.FrameSignal.WaitAsync(TimeSpan.FromSeconds(5), ct);
                while (active.FrameSignal.Wait(0)) { } // coalesce: only the newest frame matters
                if (DateTime.UtcNow - active.LastFrameUtc > FrameDeadMan)
                {
                    _logger.LogWarning("Presentation {SessionId}: no frames for {Seconds}s — ending (dead-man).",
                        active.SessionId, FrameDeadMan.TotalSeconds);
                    Stop(active.SessionId, "no frames");
                    break;
                }
                if (!signalled)
                    continue;

                var frame = active.LatestFrame;
                if (frame is null)
                    continue;
                try { await WriteAsync(pipe, MsgFrame, frame, ct); }
                catch (IOException) { break; } // helper gone → outer loop relaunches
            }
            }
            catch (OperationCanceledException) { /* stopping — fall through to send the stop message */ }

            try { await WriteAsync(pipe, MsgStop, [], CancellationToken.None); } catch { /* helper already gone */ }
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try
                {
                    // Give the helper a moment to unlock/close cleanly, then make sure it's gone.
                    if (!process.WaitForExit(2000))
                        process.Kill(entireProcessTree: true);
                }
                catch { /* best effort */ }
            }
            process?.Dispose();
        }
    }

    private static async Task WriteAsync(Stream stream, byte type, byte[] payload, CancellationToken ct)
    {
        var buffer = new byte[5 + payload.Length];
        buffer[0] = type;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 4), payload.Length);
        payload.CopyTo(buffer, 5);
        await stream.WriteAsync(buffer, ct);
        await stream.FlushAsync(ct);
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        // LocalSystem service → interactive-user helper: explicit ACL, same as RemoteControlCoordinator.
        var security = new PipeSecurity();
        security.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    public void Dispose()
    {
        Active? active;
        lock (_lock) { active = _active; _active = null; }
        active?.Cts.Cancel();
    }

    private sealed class Active(Guid sessionId, bool lockInput)
    {
        public Guid SessionId { get; } = sessionId;
        public bool LockInput { get; } = lockInput;
        public CancellationTokenSource Cts { get; } = new();
        public SemaphoreSlim FrameSignal { get; } = new(0);
        public volatile byte[]? LatestFrame;
        public DateTime LastFrameUtc = DateTime.UtcNow;
    }
}
