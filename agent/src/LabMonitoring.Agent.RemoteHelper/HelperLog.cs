namespace LabMonitoring.Agent.RemoteHelper;

/// <summary>
/// The helper runs headless (launched via <c>CreateProcessAsUser</c> with no console attached), so
/// its stdout/stderr go nowhere — without this, a crash here is invisible. Deliberately dependency-
/// free (no Serilog) since this is the one thing that must still work even if something upstream is
/// broken; writes to the same log directory the main Worker Service uses, one file per day.
/// </summary>
internal static class HelperLog
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabMonitoring", "Agent", "logs");
    private static readonly Lock WriteLock = new();

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [RemoteHelper] {message}{Environment.NewLine}";
        try
        {
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, $"remotehelper-{DateTime.Now:yyyyMMdd}.log");
            lock (WriteLock) { File.AppendAllText(path, line); }
        }
        catch (Exception ex)
        {
            // Fallback so a write failure against the shared ProgramData log dir (e.g. an ACL/
            // sharing issue specific to this process's user context) is still visible somewhere,
            // instead of vanishing into the original catch-all that swallowed everything (ADR-0013
            // — this is how we're diagnosing why some sessions produced zero ProgramData log lines).
            try
            {
                var fallbackPath = Path.Combine(Path.GetTempPath(), "remotehelper-fallback.log");
                lock (WriteLock) { File.AppendAllText(fallbackPath, line + $"  [PRIMARY WRITE FAILED: {ex}]{Environment.NewLine}"); }
            }
            catch
            {
                // Logging must never be the reason the helper crashes.
            }
        }
    }
}
