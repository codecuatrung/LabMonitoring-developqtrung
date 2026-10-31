using System.Text.Json;
using LabMonitoring.Agent.Commands;
using LabMonitoring.Agent.Configuration;
using LabMonitoring.Agent.Identity;
using LabMonitoring.Agent.Protocol;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Agent.Client;

/// <summary>
/// SignalR client for /hubs/lab. Connects with <c>Authorization: AgentToken</c>, joins the room
/// and per-machine groups server-side, and drives a command end to end (SRS 8.4/8.5): verify →
/// <c>AckCommand</c> (received) → execute → <c>POST /agent/command-result</c> (final outcome).
/// </summary>
public sealed class AgentHubClient(
    IOptions<AgentOptions> options,
    CredentialStore credentials,
    CommandReceiver commandReceiver,
    ICommandExecutor commandExecutor,
    AgentApiClient api,
    ILogger<AgentHubClient> logger) : IAsyncDisposable
{
    private readonly AgentOptions _options = options.Value;
    private HubConnection? _connection;

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    // Remote control signaling (ADR-0013) — plain events rather than a direct dependency on
    // RemoteControlCoordinator, so this class doesn't need to know that type exists (avoids a
    // circular DI dependency: the coordinator depends on this client to send messages back up).
    public event Action<Guid>? RemoteControlRequested;
    public event Action<Guid, string>? RemoteControlAnswerReceived;
    public event Action<Guid, string, string?, int?>? RemoteControlIceCandidateReceived;
    public event Action<Guid>? RemoteControlEnded;

    // Screen presentation (admin browser → whole room). Frames are display data only; the session itself
    // is authorized by the signed StartPresentation command PresentationCoordinator acts on.
    public event Action<Guid, byte[]>? PresentationFrameReceived;
    public event Action<Guid>? PresentationEnded;
    public event Action? HubReconnected;

    public Task JoinPresentationAsync(Guid sessionId, CancellationToken ct)
        => _connection?.InvokeAsync("JoinPresentation", sessionId, ct) ?? Task.CompletedTask;

    public async Task StartAsync(CancellationToken ct)
    {
        if (_connection is not null)
        {
            // Automatic reconnect gives up for good after its retry schedule (Closed state) - e.g. the
            // server was down for hours. The heartbeat loop keeps the agent looking "Online" over plain
            // HTTP while the hub stays dead and no command ever arrives, so a Closed connection must be
            // restartable here instead of being skipped forever.
            if (_connection.State == HubConnectionState.Disconnected)
            {
                await _connection.StartAsync(ct);
                logger.LogInformation("Hub connection restarted after being closed.");
            }
            return;
        }

        var creds = credentials.Load() ?? throw new InvalidOperationException("Agent is not registered.");
        var hubUrl = new Uri(new Uri(_options.ServerBaseUrl), "hubs/lab");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, o => o.Headers.Add("Authorization", $"AgentToken {creds.AgentToken}"))
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        // Default 30s ServerTimeout is too tight for a dev server under `dotnet watch` load (or a
        // congested lab Wi-Fi in production) — a few seconds' delay in the server's keep-alive ping
        // trips a false "connection dead" verdict, dropping the agent out of its computer group right
        // when a command might be delivered. 60s (still comfortably > 2x the 15s keep-alive interval,
        // per SignalR's own guidance) gives real jitter room without masking an actually-dead link for
        // more than a minute.
        _connection.ServerTimeout = TimeSpan.FromSeconds(60);

        _connection.On<JsonElement>("CommandIssued", payload => HandleCommandIssuedAsync(payload));

        _connection.On<Guid>("RemoteControlRequested", sessionId => RemoteControlRequested?.Invoke(sessionId));
        _connection.On<Guid, string>("RemoteControlAnswer", (sessionId, sdp) => RemoteControlAnswerReceived?.Invoke(sessionId, sdp));
        _connection.On<Guid, JsonElement>("RemoteControlIceCandidate", (sessionId, payload) =>
        {
            var candidate = payload.GetProperty("candidate").GetString() ?? "";
            var sdpMid = payload.TryGetProperty("sdpMid", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var sdpMLineIndex = payload.TryGetProperty("sdpMLineIndex", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : (int?)null;
            RemoteControlIceCandidateReceived?.Invoke(sessionId, candidate, sdpMid, sdpMLineIndex);
        });
        _connection.On<Guid>("RemoteControlEnded", sessionId => RemoteControlEnded?.Invoke(sessionId));
        _connection.On<Guid, byte[]>("PresentationFrame", (sessionId, jpeg) => PresentationFrameReceived?.Invoke(sessionId, jpeg));
        _connection.On<Guid>("PresentationEnded", sessionId => PresentationEnded?.Invoke(sessionId));

        _connection.Reconnected += _ => { logger.LogInformation("Hub reconnected."); HubReconnected?.Invoke(); return Task.CompletedTask; };
        _connection.Closed += ex => { logger.LogWarning(ex, "Hub connection closed."); return Task.CompletedTask; };

        await _connection.StartAsync(ct);
        logger.LogInformation("Connected to hub at {Url}", hubUrl);
    }

    private async Task HandleCommandIssuedAsync(JsonElement payload)
    {
        CommandEnvelope? command;
        try
        {
            command = await commandReceiver.VerifyAsync(payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to verify an incoming CommandIssued payload");
            return;
        }

        if (command is null)
            return; // rejected (bad signature/nonce/clock-skew) — already logged, nothing to ack

        try
        {
            if (_connection is not null)
                await _connection.InvokeAsync("AckCommand", command.CommandId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to ack command {CommandId} (continuing to execute it anyway)", command.CommandId);
        }

        // File transfers can run for minutes; SignalR delivers hub messages one at a time, so awaiting one here
        // would block every other command (Lock, Stop presentation...). Ack is already sent — finish in the background.
        if (command.Type is CommandType.PushFile or CommandType.CollectFiles)
        {
            _ = Task.Run(() => ExecuteAndReportAsync(command));
            return;
        }

        await ExecuteAndReportAsync(command);
    }

    private async Task ExecuteAndReportAsync(CommandEnvelope command)
    {
        var ct = CancellationToken.None;
        CommandExecutionResult result;
        try
        {
            result = await commandExecutor.ExecuteAsync(command, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command {CommandId} ({Type}) execution threw", command.CommandId, command.Type);
            result = CommandExecutionResult.Fail(ex.Message);
        }

        if (result.Deferred)
        {
            // UpdateAgent (SRS H1): the terminal result isn't known yet — the process that comes back
            // up after the service restarts reports it (see AgentWorker's startup pending-result check).
            logger.LogInformation("Command {CommandId} ({Type}) deferred — result will be reported after restart.", command.CommandId, command.Type);
            return;
        }

        if (result.Success)
            logger.LogInformation("Command {CommandId} ({Type}) executed.", command.CommandId, command.Type);
        else
            logger.LogWarning("Command {CommandId} ({Type}) failed: {Error}", command.CommandId, command.Type, result.ErrorMessage);

        await api.SendCommandResultAsync(new AgentCommandResultRequest
        {
            CommandId = command.CommandId,
            Status = result.Success ? CommandExecutionStatus.Executed : CommandExecutionStatus.Failed,
            ErrorMessage = result.ErrorMessage,
        }, ct);
    }

    // Remote control signaling (ADR-0013): Agent → Server relays, mirroring LabHub's SubmitOffer/
    // SubmitIceCandidate/EndRemoteControl. No-op (rather than throw) if the hub isn't connected —
    // the coordinator treats these as best-effort, same spirit as AckCommand above.
    public Task SubmitRemoteControlOfferAsync(Guid sessionId, string sdp, CancellationToken ct)
        => _connection?.InvokeAsync("SubmitOffer", sessionId, sdp, ct) ?? Task.CompletedTask;

    public Task SubmitRemoteControlIceCandidateAsync(Guid sessionId, string candidate, string? sdpMid, int? sdpMLineIndex, CancellationToken ct)
        => _connection?.InvokeAsync("SubmitIceCandidate", sessionId, candidate, sdpMid, sdpMLineIndex, ct) ?? Task.CompletedTask;

    public Task EndRemoteControlAsync(Guid sessionId, CancellationToken ct)
        => _connection?.InvokeAsync("EndRemoteControl", sessionId, ct) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}

/// <summary>Reconnect delays 0s, 2s, 10s, 30s, then every 60s forever - the SignalR default gives up
/// after 4 attempts (~1 min), leaving the agent deaf to commands after any longer server outage.</summary>
internal sealed class ForeverRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Steps = [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < Steps.Length ? Steps[retryContext.PreviousRetryCount] : TimeSpan.FromSeconds(60);
}
