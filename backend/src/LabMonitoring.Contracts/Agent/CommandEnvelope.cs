namespace LabMonitoring.Contracts.Agent;

// Control-command wire contract (SRS section 8.5). The LabAgent is a fully independent
// solution, so it keeps its own copy of this type.
// MUST stay byte-compatible with the agent copy:
//   agent/src/LabMonitoring.Agent/Protocol/CommandEnvelope.cs
// Bump ProtocolVersion on both sides for any breaking change to the shape or signing input.

/// <summary>
/// Signed control-command payload pushed to an Agent over SignalR.
/// The Agent verifies <see cref="Signature"/> with its local AgentSecret, checks the
/// nonce (replay, TTL 60s) and <see cref="IssuedAt"/> clock-skew (±5 min) before executing.
/// </summary>
public sealed record CommandEnvelope
{
    public const string ProtocolVersion = "1.1";

    /// <summary>The <c>Commands.Id</c> row this envelope was dispatched from.</summary>
    public required long CommandId { get; init; }
    public required CommandType Type { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required Guid Nonce { get; init; }

    /// <summary>base64(HMAC-SHA256(PayloadJson, AgentSecret)).</summary>
    public required string Signature { get; init; }
}

public enum CommandType
{
    Lock,
    Restart,
    Shutdown,
    Message,
    OpenApp,
    OpenUrl,
    UpdateAgent,
    StartPresentation,
    StopPresentation,
    PushFile,
    CollectFiles,
}

public enum CommandExecutionStatus
{
    Sent,
    Acknowledged,
    Executed,
    Failed,
    Timeout,
}

/// <summary>
/// Payload for <c>POST /api/v1/agent/command-result</c> — the Agent's final, durable report of
/// what happened after it finished (or gave up on) a command. <c>AckCommand</c> over SignalR is
/// the lighter pre-execution "received it" signal; this is the terminal outcome (SRS 8.2/8.5).
/// </summary>
public sealed record AgentCommandResultRequest
{
    public required long CommandId { get; init; }
    public required CommandExecutionStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}
