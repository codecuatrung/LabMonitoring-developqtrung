using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Commands;

/// <summary>
/// Runs a process in the active console user's session from a LocalSystem Windows Service — the
/// standard "service launches an interactive app" pattern (query the user's token, duplicate it,
/// build their environment block, CreateProcessAsUser onto their desktop). Used for Lock (via
/// <c>rundll32 user32.dll,LockWorkStation</c>), OpenApp and OpenUrl — none of these are
/// machine-wide like Restart/Shutdown, so they must run *in* the user's session, not Session 0.
///
/// NOT used for the remote-control helper (see <see cref="TaskSchedulerProcessLauncher"/> instead,
/// ADR-0013) — a self-contained .NET apphost launched this way consistently died with
/// STATUS_DLL_INIT_FAILED (0xC0000142) before any of its own code ran, on two unrelated physical
/// machines, for reasons never fully root-caused despite deep investigation (Process Monitor showed
/// it loading kernel32/user32/win32u/gdi32/gdi32full/shell32/imm32 successfully and querying GRE/IME
/// registry keys normally, then all its threads exiting within 1ms with no failing syscall logged).
/// Simple native launches (rundll32, explorer.exe, arbitrary installed exes like Chrome) are
/// unaffected, so this mechanism is kept as-is for Lock/OpenApp/OpenUrl.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class SessionProcessLauncher
{
    public static bool TryLaunch(string commandLine, ILogger logger, out string? error)
        => TryLaunch(commandLine, logger, out _, out error);

    /// <summary>
    /// Same as <see cref="TryLaunch(string, ILogger, out string?)"/> but also hands back a
    /// <see cref="Process"/> for the launched process instead of closing its handle immediately —
    /// needed by callers that must track the process's lifetime (e.g. react to it exiting
    /// unexpectedly).
    /// </summary>
    public static bool TryLaunch(string commandLine, ILogger logger, out Process? process, out string? error)
        => TryLaunchCore(commandLine, logger, 0, out process, out _, out error);

    /// <summary>Launches and waits up to <paramref name="waitMs"/> for exit, returning the real exit code
    /// (Process.GetProcessById can't read it — .NET refuses for processes it didn't start).</summary>
    public static bool TryLaunchAndWait(string commandLine, ILogger logger, int waitMs, out int? exitCode, out string? error)
        => TryLaunchCore(commandLine, logger, waitMs, out _, out exitCode, out error);

    private static bool TryLaunchCore(string commandLine, ILogger logger, int waitMs, out Process? process, out int? exitCode, out string? error)
    {
        process = null;
        exitCode = null;
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            error = "No user is logged into the console session.";
            return false;
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            error = $"WTSQueryUserToken failed (Win32 error {Marshal.GetLastWin32Error()}).";
            return false;
        }

        try
        {
            if (!DuplicateTokenEx(userToken, GENERIC_ALL_ACCESS, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out var primaryToken))
            {
                error = $"DuplicateTokenEx failed (Win32 error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            try
            {
                var hasEnv = CreateEnvironmentBlock(out var envBlock, primaryToken, false);
                try
                {
                    var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = "winsta0\\default" };
                    const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
                    const int CREATE_NO_WINDOW = 0x08000000;

                    var ok = CreateProcessAsUser(
                        primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                        CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW, hasEnv ? envBlock : IntPtr.Zero,
                        null, ref startupInfo, out var processInfo);

                    if (!ok)
                    {
                        error = $"CreateProcessAsUser failed (Win32 error {Marshal.GetLastWin32Error()}).";
                        return false;
                    }

                    try
                    {
                        process = Process.GetProcessById(processInfo.dwProcessId);
                    }
                    catch (ArgumentException)
                    {
                        // Process already exited before we could look it up — treat as launched;
                        // callers track lifetime best-effort only.
                    }

                    if (waitMs > 0 && WaitForSingleObject(processInfo.hProcess, (uint)waitMs) == 0 && GetExitCodeProcess(processInfo.hProcess, out var code))
                        exitCode = (int)code;

                    CloseHandle(processInfo.hProcess);
                    CloseHandle(processInfo.hThread);
                    error = null;
                    return true;
                }
                finally
                {
                    if (hasEnv) DestroyEnvironmentBlock(envBlock);
                }
            }
            finally
            {
                CloseHandle(primaryToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SessionProcessLauncher threw unexpectedly");
            error = ex.Message;
            return false;
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    private const uint GENERIC_ALL_ACCESS = 0x10000000;

    private enum SECURITY_IMPERSONATION_LEVEL { SecurityAnonymous, SecurityIdentification, SecurityImpersonation, SecurityDelegation }
    private enum TOKEN_TYPE { TokenPrimary = 1, TokenImpersonation = 2 }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateTokenEx(
        IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel, TOKEN_TYPE tokenType, out IntPtr phNewToken);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, [MarshalAs(UnmanagedType.Bool)] bool bInherit);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    // lpCommandLine must be mutable per Win32 docs; string marshaling here copies to a fresh buffer
    // each call so that's satisfied without extra ceremony.
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken, string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
        int dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
}
