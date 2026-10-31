using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.TaskScheduler;

namespace LabMonitoring.Agent.Commands;

/// <summary>
/// Launches a process into the interactive console user's session via the Windows Task Scheduler
/// (ADR-0013) — the standard, Microsoft-documented way to do this from a LocalSystem service.
///
/// Used only for the remote-control helper. <see cref="SessionProcessLauncher"/>'s own
/// WTSQueryUserToken + DuplicateTokenEx + CreateProcessAsUser approach works fine for simple native
/// launches (Lock/OpenApp/OpenUrl), but a self-contained .NET apphost launched that way consistently
/// dies with STATUS_DLL_INIT_FAILED (0xC0000142) before any of its own code runs — confirmed via
/// Process Monitor (the child loads kernel32/user32/win32u/gdi32/gdi32full/shell32/imm32 successfully
/// and queries GRE/IME registry keys normally, then all its threads exit within 1ms with no failing
/// syscall logged) and reproduced identically on two unrelated physical machines, so it isn't
/// specific to this dev machine's environment. Task Scheduler's own interactive-token launch path
/// (used internally by Windows for exactly this "service launches an app in a user's session"
/// scenario) sidesteps whatever the hand-rolled token/desktop plumbing is missing.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TaskSchedulerProcessLauncher
{
    public static bool TryLaunch(string exePath, string arguments, string? userName, ILogger logger, out Process? process, out string? error)
    {
        process = null;
        var taskName = $"LabMonitoringRemoteControl_{Guid.NewGuid():N}";

        try
        {
            using var taskService = new TaskService();
            var taskDefinition = taskService.NewTask();
            taskDefinition.RegistrationInfo.Description = "LabMonitoring remote-control helper (transient, self-deleting)";
            taskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;
            if (!string.IsNullOrWhiteSpace(userName))
                taskDefinition.Principal.UserId = userName;
            taskDefinition.Settings.Hidden = true;
            taskDefinition.Settings.DisallowStartIfOnBatteries = false;
            taskDefinition.Settings.StopIfGoingOnBatteries = false;
            taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
            taskDefinition.Actions.Add(new ExecAction(exePath, arguments));

            var registeredTask = taskService.RootFolder.RegisterTaskDefinition(taskName, taskDefinition);
            try
            {
                var runningTask = registeredTask.Run();
                var pid = runningTask.EnginePID;
                if (pid > 0)
                {
                    try { process = Process.GetProcessById((int)pid); }
                    catch (ArgumentException)
                    {
                        // Already exited before we could look it up — treat as launched; caller
                        // tracks lifetime best-effort only (same contract as SessionProcessLauncher).
                    }
                }
                error = null;
                return true;
            }
            finally
            {
                // The task definition is only a vehicle to get one interactive-session launch — the
                // running instance is independent of it once started, so deleting it immediately
                // doesn't affect the child process and keeps Task Scheduler's task list clean.
                try { taskService.RootFolder.DeleteTask(taskName, exceptionOnNotExists: false); }
                catch (Exception ex) { logger.LogDebug(ex, "Failed to delete transient task {TaskName} (harmless).", taskName); }
            }
        }
        catch (Exception ex)
        {
            // ToString() (not just Message) — this library's failures are often a wrapped
            // TypeInitializationException where the real cause is in InnerException.
            error = ex.ToString();
            return false;
        }
    }
}
