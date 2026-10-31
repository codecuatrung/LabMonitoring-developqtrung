using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Monitoring;

/// <summary>
/// Reports the interactive Windows user on the active console session (SRS FR-1.4). Runs from a
/// LocalSystem service, so <c>Environment.UserName</c> is not usable — this uses the WTS API.
/// </summary>
public sealed partial class SessionInfoProvider(ILogger<SessionInfoProvider> logger)
{
    public string? CurrentConsoleUser()
    {
        if (!OperatingSystem.IsWindows())
            return Environment.UserName; // dev / Linux fallback

        return QueryWindowsConsoleUser();
    }

    [SupportedOSPlatform("windows")]
    private string? QueryWindowsConsoleUser()
    {
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                logger.LogInformation("QueryWindowsConsoleUser: no console session attached (0xFFFFFFFF).");
                return null; // no session attached
            }

            var queryOk = WTSQuerySessionInformation(nint.Zero, sessionId, WTS_INFO_CLASS.WTSUserName, out var buffer, out var bytes);
            if (!queryOk || bytes <= 2)
            {
                logger.LogInformation("QueryWindowsConsoleUser: sessionId={SessionId}, queryOk={QueryOk}, bytes={Bytes}, win32err={Win32Err}.",
                    sessionId, queryOk, bytes, queryOk ? 0 : Marshal.GetLastWin32Error());
                return null;
            }

            try
            {
                var user = Marshal.PtrToStringUni(buffer);
                if (string.IsNullOrWhiteSpace(user))
                    return null;

                if (WTSQuerySessionInformation(nint.Zero, sessionId, WTS_INFO_CLASS.WTSDomainName, out var domainBuf, out _))
                {
                    var domain = Marshal.PtrToStringUni(domainBuf);
                    WTSFreeMemory(domainBuf);
                    return string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
                }

                return user;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "WTS console user query failed");
            return null;
        }
    }

    private enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7,
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    // [LibraryImport] does NOT do the classic DllImport ExactSpelling=false A/W-suffix probing —
    // the real export is "WTSQuerySessionInformationW"; without an explicit EntryPoint this threw
    // EntryPointNotFoundException on every call, which QueryWindowsConsoleUser silently swallowed
    // (LogDebug, invisible at the default Information level) making CurrentConsoleUser() always
    // return null (ADR-0013 — this is also why the remote-control helper always launched under
    // Task Scheduler's SYSTEM fallback instead of the real interactive user, which is what made
    // BitBlt fail with ERROR_INVALID_HANDLE: SYSTEM can't read another session's DWM-composited
    // desktop).
    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(nint hServer, uint sessionId, WTS_INFO_CLASS infoClass, out nint ppBuffer, out uint pBytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint pMemory);
}
