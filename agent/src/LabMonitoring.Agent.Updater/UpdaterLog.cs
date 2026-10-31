namespace LabMonitoring.Agent.Updater;

/// <summary>
/// This process runs detached with no console (launched via <c>Process.Start</c> from the Agent
/// service, <c>CreateNoWindow = true</c>) so its own stdout goes nowhere — without this, a failure
/// here would be invisible. Deliberately dependency-free, same reasoning as
/// <c>LabMonitoring.Agent.RemoteHelper</c>'s <c>HelperLog</c>: writes to the same log directory the
/// Worker Service uses, one file per day.
/// </summary>
internal static class UpdaterLog
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabMonitoring", "Agent", "logs");
    private static readonly Lock WriteLock = new();

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [Updater] {message}{Environment.NewLine}";
            var path = Path.Combine(LogDir, $"updater-{DateTime.Now:yyyyMMdd}.log");
            lock (WriteLock) { File.AppendAllText(path, line); }
        }
        catch
        {
            // Logging must never be the reason the updater crashes.
        }
    }
}
