namespace LabMonitoring.Agent.Protocol;

// Control-command wire contract (SRS section 8.5). The agent is fully independent of the
// backend solution, so this type is duplicated here on purpose.
// MUST stay byte-compatible with the backend copy:
//   backend/src/LabMonitoring.Contracts/Agent/CommandEnvelope.cs
// Bump ProtocolVersion on both sides for any breaking change to the shape or signing input.

/// <summary>
/// Signed control-command payload pushed to an Agent over SignalR.
/// The Agent verifies <see cref="Signature"/> with its local AgentSecret, checks the
/// nonce (replay, TTL 60s) and <see cref="IssuedAt"/> clock-skew (±5 min) before executing.
/// </summary>
public sealed record CommandEnvelope
{
    public const string ProtocolVersion = "1.1";

    /// <summary>The server's <c>Commands.Id</c> row this envelope was dispatched from.</summary>
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
