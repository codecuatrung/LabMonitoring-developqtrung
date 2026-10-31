using System.Diagnostics;

namespace LabMonitoring.Agent.Updater;

/// <summary>Shells out to <c>sc.exe</c> for service start/stop/status — the exact same tool
/// <c>install-service.ps1</c>/<c>uninstall-service.ps1</c> already use, so behaviour matches what a
/// technician sees running those scripts by hand. Avoids taking on the
/// <c>System.ServiceProcess.ServiceController</c> package for something this small.</summary>
internal static class ServiceControl
{
    public static bool IsRunning(string serviceName) => QueryState(serviceName)?.Contains("RUNNING") == true;

    public static bool Stop(string serviceName, TimeSpan timeout)
    {
        if (!IsRunning(serviceName))
            return true;

        Run("stop", serviceName);
        return WaitUntil(() => !IsRunning(serviceName), timeout);
    }

    public static bool Start(string serviceName, TimeSpan timeout)
    {
        Run("start", serviceName);
        return WaitUntil(() => IsRunning(serviceName), timeout);
    }

    /// <summary>True if the service is (still) running after waiting out the window — catches a
    /// service that starts and then crashes moments later, which a one-shot IsRunning() check right
    /// after Start() would miss.</summary>
    public static bool StaysUpFor(string serviceName, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning(serviceName))
                return false;
            Thread.Sleep(1000);
        }
        return IsRunning(serviceName);
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(1000);
        }
        return condition();
    }

    private static string? QueryState(string serviceName)
    {
        var (exitCode, output) = Run("query", serviceName);
        if (exitCode != 0)
            return null; // e.g. service doesn't exist — treat as "not running"

        var line = output.Split('\n').FirstOrDefault(l => l.Contains("STATE"));
        return line?.Trim();
    }

    private static (int ExitCode, string Output) Run(string verb, string serviceName)
    {
        var psi = new ProcessStartInfo("sc.exe", $"{verb} \"{serviceName}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        UpdaterLog.Write($"sc.exe {verb} \"{serviceName}\" -> exit={process.ExitCode}: {output.Trim().ReplaceLineEndings(" | ")}");
        return (process.ExitCode, output);
    }
}
