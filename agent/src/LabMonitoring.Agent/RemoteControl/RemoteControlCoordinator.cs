using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LabMonitoring.Agent.Client;
using LabMonitoring.Agent.Commands;
using LabMonitoring.Agent.Monitoring;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.RemoteControl;

/// <summary>
/// Owns the lifecycle of a remote-control session on the Agent side (ADR-0013): launches the
/// RemoteHelper process into the interactive session via <see cref="TaskSchedulerProcessLauncher"/>
/// (since screen capture and input injection must run there, not in this LocalSystem/Session 0
/// process), relays SDP/ICE between it and <see cref="AgentHubClient"/> over a local named pipe, and
/// cleans up if either side disconnects or the helper crashes.
///
/// Registered as a singleton and resolved once at startup purely to wire its constructor's event
/// subscriptions onto <see cref="AgentHubClient"/> (see Program.cs) — it has no other entry point.
/// </summary>
public sealed class RemoteControlCoordinator : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AgentHubClient _hub;
    private readonly SessionInfoProvider _sessionInfo;
    private readonly ILogger<RemoteControlCoordinator> _logger;
    private readonly object _lock = new();
    private ActiveSession? _active;

    public RemoteControlCoordinator(AgentHubClient hub, SessionInfoProvider sessionInfo, ILogger<RemoteControlCoordinator> logger)
    {
        _hub = hub;
        _sessionInfo = sessionInfo;
        _logger = logger;

        _hub.RemoteControlRequested += sessionId => _ = SafeAsync(() => HandleRequestedAsync(sessionId), sessionId, "requested");
        _hub.RemoteControlAnswerReceived += (sessionId, sdp) => _ = SafeAsync(() => HandleAnswerAsync(sessionId, sdp), sessionId, "answer");
        _hub.RemoteControlIceCandidateReceived += (sessionId, candidate, sdpMid, sdpMLineIndex) =>
            _ = SafeAsync(() => HandleRemoteIceAsync(sessionId, candidate, sdpMid, sdpMLineIndex), sessionId, "ice candidate");
        _hub.RemoteControlEnded += sessionId => _ = SafeAsync(() => HandleEndedAsync(sessionId), sessionId, "ended");
    }

    private async Task SafeAsync(Func<Task> action, Guid sessionId, string what)
    {
        try { await action(); }
        catch (Exception ex) { _logger.LogError(ex, "Remote control session {SessionId}: handling '{What}' threw", sessionId, what); }
    }

    private async Task HandleRequestedAsync(Guid sessionId)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Remote control requested ({SessionId}) but this platform is not supported.", sessionId);
            await _hub.EndRemoteControlAsync(sessionId, CancellationToken.None);
            return;
        }

        lock (_lock)
        {
            if (_active is not null)
            {
                _logger.LogWarning("Remote control requested ({SessionId}) while session {Existing} is already active — ignoring.", sessionId, _active.SessionId);
                return;
            }
        }

        var pipeName = $"LabMonitoringAgent.RemoteControl.{sessionId:N}";
        var pipe = CreatePipeServer(pipeName);

        var helperExe = Path.Combine(AppContext.BaseDirectory, "RemoteHelper", "LabMonitoring.Agent.RemoteHelper.exe");
        var arguments = $"--session {sessionId} --pipe {pipeName}";
        var userName = _sessionInfo.CurrentConsoleUser();

        if (!TaskSchedulerProcessLauncher.TryLaunch(helperExe, arguments, userName, _logger, out var process, out var error))
        {
            _logger.LogError("Failed to launch remote control helper for session {SessionId}: {Error}", sessionId, error);
            pipe.Dispose();
            await _hub.EndRemoteControlAsync(sessionId, CancellationToken.None);
            return;
        }

        _logger.LogInformation("Remote control session {SessionId}: helper launched (pid {Pid}) via Task Scheduler for user {UserName}.",
            sessionId, process?.Id, userName);

        var cts = new CancellationTokenSource();
        var session = new ActiveSession(sessionId, pipe, process, cts);
        lock (_lock) { _active = session; }

        if (process is not null)
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _logger.LogInformation(
                "Remote control session {SessionId}: helper process exited with code {ExitCode}.", sessionId, SafeExitCode(process));
            process.Exited += (_, _) => _ = SafeAsync(() => HandleHelperExitedAsync(sessionId), sessionId, "helper exited");
        }

        _ = RunPipeSessionAsync(session);
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return null; }
    }

    private async Task RunPipeSessionAsync(ActiveSession session)
    {
        try
        {
            _logger.LogInformation("Remote control session {SessionId}: waiting for helper to connect to pipe.", session.SessionId);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(session.Cts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));
            await session.Pipe.WaitForConnectionAsync(connectCts.Token);
            _logger.LogInformation("Remote control session {SessionId}: helper connected to pipe.", session.SessionId);

            session.Writer = new StreamWriter(session.Pipe, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            using var reader = new StreamReader(session.Pipe, leaveOpen: true);

            while (!session.Cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(session.Cts.Token);
                if (line is null)
                    break; // helper closed the pipe

                await HandleHelperMessageAsync(session, line);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on session end / connect timeout
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remote control session {SessionId}: pipe loop ended unexpectedly.", session.SessionId);
        }
        finally
        {
            await CleanUpAsync(session, notifyServer: true);
        }
    }

    private async Task HandleHelperMessageAsync(ActiveSession session, string line)
    {
        PipeMessage? message;
        try { message = JsonSerializer.Deserialize<PipeMessage>(line, JsonOpts); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Remote control session {SessionId}: malformed message from helper.", session.SessionId);
            return;
        }
        if (message is null)
            return;

        _logger.LogInformation("Remote control session {SessionId}: received '{Type}' from helper.", session.SessionId, message.Type);

        switch (message.Type)
        {
            case "offer" when message.Sdp is not null:
                await _hub.SubmitRemoteControlOfferAsync(session.SessionId, message.Sdp, CancellationToken.None);
                break;
            case "localIce" when message.Candidate is not null:
                await _hub.SubmitRemoteControlIceCandidateAsync(
                    session.SessionId, message.Candidate, message.SdpMid, message.SdpMLineIndex, CancellationToken.None);
                break;
            default:
                _logger.LogWarning("Remote control session {SessionId}: unknown message type '{Type}' from helper.", session.SessionId, message.Type);
                break;
        }
    }

    private async Task HandleAnswerAsync(Guid sessionId, string sdp)
    {
        var session = GetActive(sessionId);
        if (session?.Writer is null)
            return;

        await session.WriteLineAsync(JsonSerializer.Serialize(new PipeMessage { Type = "answer", Sdp = sdp }, JsonOpts));
    }

    private async Task HandleRemoteIceAsync(Guid sessionId, string candidate, string? sdpMid, int? sdpMLineIndex)
    {
        var session = GetActive(sessionId);
        if (session?.Writer is null)
            return;

        await session.WriteLineAsync(JsonSerializer.Serialize(
            new PipeMessage { Type = "remoteIce", Candidate = candidate, SdpMid = sdpMid, SdpMLineIndex = sdpMLineIndex }, JsonOpts));
    }

    private async Task HandleEndedAsync(Guid sessionId)
    {
        var session = GetActive(sessionId);
        if (session is null)
            return;

        try
        {
            if (session.Writer is not null)
                await session.WriteLineAsync(JsonSerializer.Serialize(new PipeMessage { Type = "stop" }, JsonOpts));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Remote control session {SessionId}: failed to send stop to helper (already gone?).", sessionId);
        }

        await CleanUpAsync(session, notifyServer: false);
    }

    private async Task HandleHelperExitedAsync(Guid sessionId)
    {
        var session = GetActive(sessionId);
        if (session is null)
            return;

        _logger.LogInformation("Remote control session {SessionId}: helper process exited.", sessionId);
        await CleanUpAsync(session, notifyServer: true);
    }

    private ActiveSession? GetActive(Guid sessionId)
    {
        lock (_lock)
            return _active?.SessionId == sessionId ? _active : null;
    }

    private async Task CleanUpAsync(ActiveSession session, bool notifyServer)
    {
        lock (_lock)
        {
            if (_active?.SessionId != session.SessionId)
                return; // already cleaned up by another path
            _active = null;
        }

        session.Cts.Cancel();
        try { session.Writer?.Dispose(); } catch { /* best effort */ }
        try { session.Pipe.Dispose(); } catch { /* best effort */ }

        if (session.HelperProcess is { HasExited: false } proc)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
        session.HelperProcess?.Dispose();
        session.Cts.Dispose();

        if (notifyServer)
        {
            try { await _hub.EndRemoteControlAsync(session.SessionId, CancellationToken.None); }
            catch (Exception ex) { _logger.LogDebug(ex, "Remote control session {SessionId}: failed to notify server of end.", session.SessionId); }
        }
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        // The Worker Service runs as LocalSystem (Session 0) while the helper runs impersonating the
        // interactive user's token (Session 1) — a default-ACL pipe would refuse that connection, so
        // grant local interactive users explicit read/write.
        var security = new PipeSecurity();
        security.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    public void Dispose()
    {
        ActiveSession? session;
        lock (_lock) { session = _active; }
        if (session is not null)
            CleanUpAsync(session, notifyServer: false).GetAwaiter().GetResult();
    }

    private sealed class ActiveSession(Guid sessionId, NamedPipeServerStream pipe, Process? helperProcess, CancellationTokenSource cts)
    {
        public Guid SessionId { get; } = sessionId;
        public NamedPipeServerStream Pipe { get; } = pipe;
        public Process? HelperProcess { get; } = helperProcess;
        public CancellationTokenSource Cts { get; } = cts;
        public StreamWriter? Writer { get; set; }

        private readonly SemaphoreSlim _writeGate = new(1, 1);

        /// <summary>Serializes writes to the helper pipe — SignalR delivers ICE candidates concurrently,
        /// and StreamWriter throws "stream is in use by a previous operation" on overlapping writes.</summary>
        public async Task WriteLineAsync(string line)
        {
            if (Writer is null)
                return;
            await _writeGate.WaitAsync();
            try { await Writer.WriteLineAsync(line); }
            finally { _writeGate.Release(); }
        }
    }
}
