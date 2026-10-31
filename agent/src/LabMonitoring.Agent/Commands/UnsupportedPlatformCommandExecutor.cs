using LabMonitoring.Agent.Protocol;

namespace LabMonitoring.Agent.Commands;

/// <summary>
/// Non-Windows fallback. LabAgent targets Windows 10/11 for MVP (CLAUDE.md); Linux command
/// execution (Lock/Restart/Shutdown/Message/OpenApp/OpenUrl equivalents) is explicit future work,
/// not silently unsupported — every command still gets a clear Failed result back to the server.
/// </summary>
public sealed class UnsupportedPlatformCommandExecutor : ICommandExecutor
{
    public Task<CommandExecutionResult> ExecuteAsync(CommandEnvelope command, CancellationToken ct)
        => Task.FromResult(CommandExecutionResult.Fail(
            $"Command execution is not implemented on this OS yet (Windows-only for MVP)."));
}
