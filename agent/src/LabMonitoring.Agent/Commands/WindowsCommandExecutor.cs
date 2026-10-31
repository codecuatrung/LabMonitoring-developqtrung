using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using LabMonitoring.Agent.Configuration;
using LabMonitoring.Agent.Monitoring;
using LabMonitoring.Agent.Protocol;
using LabMonitoring.Agent.RemoteControl;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Commands;

/// <summary>
/// Executes verified commands on Windows (SRS FR-4.1). Restart/Shutdown are machine-wide and run
/// directly as LocalSystem; Lock/OpenApp/OpenUrl must happen *in* the interactive user's session,
/// so they go through <see cref="SessionProcessLauncher"/>; Message uses WTSSendMessage, which is
/// built for exactly this (a service notifying a session) and needs no session launch.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsCommandExecutor(
    AgentConfigCache configCache, AgentUpdateExecutor updateExecutor, FileTransferExecutor fileTransfer, SessionInfoProvider sessionInfo,
    Func<PresentationCoordinator> presentation, ILogger<WindowsCommandExecutor> logger) : ICommandExecutor
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Grace period before a Restart/Shutdown actually happens, so a logged-in student can save work.</summary>
    private static readonly TimeSpan ShutdownDelay = TimeSpan.FromSeconds(30);

    public async Task<CommandExecutionResult> ExecuteAsync(CommandEnvelope command, CancellationToken ct)
    {
        try
        {
            // UpdateAgent (SRS H1) is the one command kind that needs real async work (downloading a
            // package) and can defer its terminal result — everything else stays synchronous.
            if (command.Type == CommandType.UpdateAgent)
                return await updateExecutor.ExecuteAsync(command.CommandId, command.PayloadJson, ct);

            if (command.Type is CommandType.StartPresentation or CommandType.StopPresentation)
                return await ExecutePresentationAsync(command);

            // Long-running (minutes): AgentHubClient runs these off the SignalR handler thread.
            if (command.Type is CommandType.PushFile or CommandType.CollectFiles)
                return await fileTransfer.ExecuteAsync(command.Type, command.PayloadJson, ct);

            return command.Type switch
            {
                CommandType.Lock => Lock(),
                CommandType.Restart => RunShutdownExe(restart: true),
                CommandType.Shutdown => RunShutdownExe(restart: false),
                CommandType.Message => SendMessage(command.PayloadJson),
                CommandType.OpenApp => OpenApp(command.PayloadJson),
                CommandType.OpenUrl => OpenUrl(command.PayloadJson),
                _ => CommandExecutionResult.Fail($"Unknown command type '{command.Type}'."),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command {CommandId} ({Type}) threw", command.CommandId, command.Type);
            return CommandExecutionResult.Fail(ex.Message);
        }
    }

    private async Task<CommandExecutionResult> ExecutePresentationAsync(CommandEnvelope command)
    {
        var payload = JsonSerializer.Deserialize<PresentationPayload>(command.PayloadJson, JsonOpts);
        if (payload is null || payload.SessionId == Guid.Empty)
            return CommandExecutionResult.Fail("Malformed presentation payload.");

        return command.Type == CommandType.StartPresentation
            ? await presentation().StartAsync(payload.SessionId, payload.LockInput)
            : presentation().Stop(payload.SessionId, "stop command");
    }

    private CommandExecutionResult Lock()
    {
        // Task Scheduler (InteractiveToken), same mechanism the remote-control helper uses: launching
        // rundll32 via CreateProcessAsUser + a duplicated token ran fine (exit 0) on real machines yet
        // LockWorkStation silently did nothing (rundll32 exits 0 even when the call inside is refused).
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var user = sessionInfo.CurrentConsoleUser();
        if (string.IsNullOrWhiteSpace(user))
            return CommandExecutionResult.Fail("No interactive user is logged in on the console.");

        return TaskSchedulerProcessLauncher.TryLaunch(
            Path.Combine(system32, "rundll32.exe"), "user32.dll,LockWorkStation", user, logger, out _, out var error)
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail(error ?? "Lock failed.");
    }
    private static CommandExecutionResult RunShutdownExe(bool restart)
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var flag = restart ? "/r" : "/s";
        var seconds = (int)ShutdownDelay.TotalSeconds;
        var message = restart
            ? "Máy sẽ khởi động lại theo yêu cầu quản trị viên phòng máy."
            : "Máy sẽ tắt theo yêu cầu quản trị viên phòng máy.";

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(system32, "shutdown.exe"),
            Arguments = $"{flag} /t {seconds} /c \"{message}\" /d p:4:1",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        return process is not null
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail("Failed to start shutdown.exe.");
    }

    private CommandExecutionResult SendMessage(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<MessagePayload>(payloadJson, JsonOpts);
        if (string.IsNullOrWhiteSpace(payload?.Text))
            return CommandExecutionResult.Fail("Message text is empty.");

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
            return CommandExecutionResult.Fail("No user is logged into the console session.");

        const string title = "Thông báo từ phòng máy";
        var sent = WTSSendMessage(IntPtr.Zero, sessionId, title, title.Length * 2,
            payload.Text, payload.Text.Length * 2, MB_OK | MB_ICONINFORMATION, 0, out _, bWait: false);

        return sent
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail($"WTSSendMessage failed (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private CommandExecutionResult OpenApp(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<OpenAppPayload>(payloadJson, JsonOpts);
        if (string.IsNullOrWhiteSpace(payload?.ExecutablePath))
            return CommandExecutionResult.Fail("Executable path is empty.");

        // Defense in depth: the server already checked the whitelist at issue time (FR-4.4); the
        // agent re-checks against its own locally-synced copy before actually launching anything.
        if (!WhitelistChecker.IsAppAllowed(payload.ExecutablePath, configCache.Current))
            return CommandExecutionResult.Fail($"'{payload.ExecutablePath}' is not in the locally synced whitelist.");

        var commandLine = $"\"{payload.ExecutablePath}\"";
        return SessionProcessLauncher.TryLaunch(commandLine, logger, out var error)
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail(error ?? "OpenApp failed.");
    }

    private CommandExecutionResult OpenUrl(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<OpenUrlPayload>(payloadJson, JsonOpts);
        if (string.IsNullOrWhiteSpace(payload?.Url))
            return CommandExecutionResult.Fail("URL is empty.");

        if (!WhitelistChecker.IsUrlAllowed(payload.Url, configCache.Current))
            return CommandExecutionResult.Fail($"'{payload.Url}' is not in the locally synced whitelist.");

        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var commandLine = $"\"{Path.Combine(system32, "..", "explorer.exe")}\" \"{payload.Url}\"";
        return SessionProcessLauncher.TryLaunch(commandLine, logger, out var error)
            ? CommandExecutionResult.Ok()
            : CommandExecutionResult.Fail(error ?? "OpenUrl failed.");
    }

    private const int MB_OK = 0x00000000;
    private const int MB_ICONINFORMATION = 0x00000040;

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSSendMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSSendMessage(
        IntPtr hServer, uint sessionId, string title, int titleLength, string message, int messageLength,
        int style, int timeoutSeconds, out int response, [MarshalAs(UnmanagedType.Bool)] bool bWait);
}
