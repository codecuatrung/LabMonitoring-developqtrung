using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace LabMonitoring.Agent.RemoteHelper;

/// <summary>
/// Injects mouse/keyboard events received over the data channel via Win32 <c>SendInput</c>
/// (ADR-0013). Works correctly here — unlike from the main Worker Service — because this process
/// runs inside the target interactive session (launched via <c>SessionProcessLauncher</c>), the same
/// reason Lock/OpenApp/OpenUrl work from that mechanism.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class InputInjector
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [ThreadStatic]
    private static bool _threadDesktopAttached;

    /// <summary>
    /// SendInput was silently succeeding (nonzero return, no Win32 error) yet the cursor never
    /// actually moved when driven from this process (ADR-0013). Screen *reading* (BitBlt/GetDC)
    /// only needs window-station access, which this process already has, but input *injection* is
    /// additionally gated on the calling thread being attached to the current input desktop via
    /// SetThreadDesktop — the callback thread that delivers data-channel messages (a SIPSorcery/
    /// threadpool thread) isn't guaranteed to have that even though the thread that showed the
    /// WinForms banner does. Attaching explicitly, once per thread, is cheap and safe to call
    /// redundantly.
    /// </summary>
    private static void EnsureThreadDesktopAttached()
    {
        if (_threadDesktopAttached)
            return;
        _threadDesktopAttached = true;

        var desktop = OpenInputDesktop(0, false, DESKTOP_ALL_ACCESS);
        if (desktop == IntPtr.Zero)
        {
            HelperLog.Write($"EnsureThreadDesktopAttached: OpenInputDesktop failed, win32err={Marshal.GetLastWin32Error()}.");
            return;
        }

        var ok = SetThreadDesktop(desktop);
        HelperLog.Write($"EnsureThreadDesktopAttached: SetThreadDesktop ok={ok}, win32err={(ok ? 0 : Marshal.GetLastWin32Error())}.");
        CloseDesktop(desktop);
    }

    public void HandleMessage(string json)
    {
        EnsureThreadDesktopAttached();
        RemoteInputEvent? evt;
        try { evt = JsonSerializer.Deserialize<RemoteInputEvent>(json, JsonOpts); }
        catch (JsonException) { return; }
        if (evt is null)
            return;

        switch (evt.Type)
        {
            case "move" when evt.X is { } x && evt.Y is { } y:
                MoveTo(x, y);
                break;
            case "down" when evt.Button is not null:
                MouseButton(evt.Button, down: true);
                break;
            case "up" when evt.Button is not null:
                MouseButton(evt.Button, down: false);
                break;
            case "wheel" when evt.DeltaY is { } deltaY:
                Wheel(deltaY);
                break;
            case "key" when evt.Down is { } down && evt.VkCode is { } vk:
                Key(vk, down);
                break;
        }
    }

    private static DateTime _lastMoveLogAt = DateTime.MinValue;
    private static int _moveLogCount;
    private static int _buttonLogCount;
    private const int MoveLogMax = 60;

    private static void MoveTo(double normalizedX, double normalizedY)
    {
        // Absolute coordinates for SendInput are 0..65535 across the addressed desktop; combined
        // with MOUSEEVENTF_VIRTUALDESK that desktop is the full multi-monitor virtual screen — the
        // same bounds ScreenCapture captures, so the normalized [0,1] input the browser sends lines
        // up with what it's actually looking at regardless of monitor layout.
        var absX = (int)(Math.Clamp(normalizedX, 0, 1) * 65535);
        var absY = (int)(Math.Clamp(normalizedY, 0, 1) * 65535);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                },
            },
        };
        Send(input);

        // Time-throttled (not count-of-first-N) so the log samples the WHOLE session, not just the
        // initial burst — needed to see whether injection starts working after some warm-up period
        // (ADR-0013 input-injection diagnosis: user reported intermittent "sometimes moves" behavior
        // that a first-5-calls-only sample couldn't have shown either way).
        var now = DateTime.UtcNow;
        if (_moveLogCount < MoveLogMax && (now - _lastMoveLogAt).TotalMilliseconds > 300)
        {
            _moveLogCount++;
            _lastMoveLogAt = now;
            GetCursorPos(out var pt);
            var vx = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var vy = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            HelperLog.Write($"MoveTo({normalizedX:F3},{normalizedY:F3}) -> abs=({absX},{absY}) -> actual cursor pos=({pt.X},{pt.Y}), virtualScreen={vx}x{vy}.");
        }
    }

    private static void MouseButton(string button, bool down)
    {
        var flag = (button, down) switch
        {
            ("left", true) => MOUSEEVENTF_LEFTDOWN,
            ("left", false) => MOUSEEVENTF_LEFTUP,
            ("right", true) => MOUSEEVENTF_RIGHTDOWN,
            ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN,
            ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            _ => 0u,
        };
        if (flag == 0)
            return;

        Send(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag } } });

        if (_buttonLogCount++ < 40)
        {
            GetCursorPos(out var pt);
            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out var fgPid);
            var title = new System.Text.StringBuilder(256);
            GetWindowText(fg, title, title.Capacity);
            HelperLog.Write($"MouseButton({button}, down={down}) at cursor=({pt.X},{pt.Y}), foregroundPid={fgPid}, foregroundTitle=\"{title}\", ourPid={Environment.ProcessId}.");
        }
    }

    private static void Wheel(double deltaY)
    {
        // Browser wheel deltaY is "pixels scrolled down"; Win32 wheel delta is in units of
        // WHEEL_DELTA (120) and positive means scroll up (away from the user) — invert and scale.
        var wheelDelta = (int)Math.Clamp(-deltaY, -1200, 1200);
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = wheelDelta } },
        });
    }

    private static void Key(int vkCode, bool down)
    {
        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = (ushort)vkCode, dwFlags = down ? 0u : KEYEVENTF_KEYUP },
            },
        });
    }

    private static int _sendLogCount;

    private static void Send(INPUT input)
    {
        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent == 0 && _sendLogCount++ < 10)
        {
            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out var fgPid);
            HelperLog.Write($"SendInput FAILED (type={input.type}, flags={input.U.mi.dwFlags}): win32err={Marshal.GetLastWin32Error()}, " +
                $"foregroundPid={fgPid}, ourPid={Environment.ProcessId}.");
        }
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const uint DESKTOP_ALL_ACCESS = 0x01FF;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
}
