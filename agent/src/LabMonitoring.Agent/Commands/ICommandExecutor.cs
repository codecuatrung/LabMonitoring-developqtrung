using LabMonitoring.Agent.Protocol;

namespace LabMonitoring.Agent.Commands;

public sealed record CommandExecutionResult(bool Success, string? ErrorMessage, bool Deferred = false)
{
    public static CommandExecutionResult Ok() => new(true, null);
    public static CommandExecutionResult Fail(string error) => new(false, error);

    /// <summary>The command was accepted and is running, but its terminal outcome isn't known yet —
    /// <c>AgentHubClient</c> must NOT call <c>SendCommandResultAsync</c> for this result. Used only by
    /// <c>UpdateAgent</c> (SRS H1): the process that reports Executed/Failed is the one that comes
    /// back up after the service restarts, not this one.</summary>
    public static CommandExecutionResult DeferredResult() => new(true, null, Deferred: true);
}

/// <summary>Performs the control actions (SRS FR-4.1) after <see cref="CommandReceiver"/> has
/// already verified the envelope's signature/nonce/clock-skew.</summary>
public interface ICommandExecutor
{
    Task<CommandExecutionResult> ExecuteAsync(CommandEnvelope command, CancellationToken ct);
}
