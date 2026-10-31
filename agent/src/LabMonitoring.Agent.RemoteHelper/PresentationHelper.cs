using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LabMonitoring.Agent.RemoteHelper;

/// <summary>
/// "Present" mode of the helper: shows the admin's shared screen full-screen on every monitor and
/// (optionally) swallows all keyboard/mouse input for as long as the session lasts. The Worker
/// Service feeds it length-prefixed messages over a named pipe: [type:1][length:4 LE][payload] with
/// type 1 = JPEG frame, 2 = stop. Every failure path ends in a full unlock — a locked-out student PC
/// is the worst outcome of this feature, so: pipe closed, stop message, or no message for
/// <see cref="SilenceLimit"/> all close the windows and remove the hooks; and if this process is
/// killed the OS removes the hooks with it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PresentationHelper
{
    private const byte MsgFrame = 1;
    private const byte MsgStop = 2;
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(45);

    private readonly string _pipeName;
    private readonly bool _lockInput;
    private readonly List<PictureBox> _boxes = [];
    private readonly List<Form> _forms = [];
    private DateTime _lastMessageUtc = DateTime.UtcNow;
    private volatile bool _closing;

    // Delegates must stay referenced for as long as the hooks are installed.
    private LowLevelProc? _keyboardProc;
    private LowLevelProc? _mouseProc;
    private IntPtr _keyboardHook, _mouseHook;

    public PresentationHelper(string pipeName, bool lockInput)
    {
        _pipeName = pipeName;
        _lockInput = lockInput;
    }

    public void Run()
    {
        var thread = new Thread(RunUi) { IsBackground = false };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private void RunUi()
    {
        ApplicationConfiguration.Initialize();

        var placeholder = "Đang chờ hình từ máy giảng viên...";
        foreach (var screen in Screen.AllScreens)
        {
            var box = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            var label = new Label
            {
                Text = placeholder, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White, BackColor = Color.Black, Font = new Font("Segoe UI", 20F),
            };
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = screen.Bounds,
                TopMost = true,
                ShowInTaskbar = false,
                BackColor = Color.Black,
                Tag = label,
            };
            form.Controls.Add(box);
            form.Controls.Add(label);
            label.BringToFront();
            _boxes.Add(box);
            _forms.Add(form);
        }

        var context = new ApplicationContext();
        foreach (var f in _forms)
            f.Show();

        if (_lockInput)
        {
            InstallHooks();
            Cursor.Hide();
        }

        // Keep the overlay on top (other topmost windows, focus games) and enforce the silence limit.
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow - _lastMessageUtc > SilenceLimit)
            {
                HelperLog.Write("Presentation: no message from the agent for too long — unlocking.");
                context.ExitThread();
                return;
            }
            foreach (var f in _forms)
            {
                f.TopMost = false;
                f.TopMost = true;
                if (_lockInput)
                    f.Activate();
            }
        };
        timer.Start();

        var reader = new Thread(() => ReadPipe(context)) { IsBackground = true };
        reader.Start();

        try { Application.Run(context); }
        finally
        {
            _closing = true;
            timer.Stop();
            RemoveHooks();
            if (_lockInput) Cursor.Show();
            foreach (var f in _forms)
                try { f.Close(); f.Dispose(); } catch { /* shutting down */ }
        }
    }

    private void ReadPipe(ApplicationContext context)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.In);
            pipe.Connect(10_000);
            HelperLog.Write("Presentation: connected to agent pipe.");

            var header = new byte[5];
            while (!_closing)
            {
                if (!ReadExactly(pipe, header, 5))
                    break; // agent closed the pipe
                _lastMessageUtc = DateTime.UtcNow;

                var length = BitConverter.ToInt32(header, 1);
                if (length < 0 || length > 16 * 1024 * 1024)
                    break;
                var payload = new byte[length];
                if (length > 0 && !ReadExactly(pipe, payload, length))
                    break;

                if (header[0] == MsgStop)
                    break;
                if (header[0] == MsgFrame && length > 0)
                    ShowFrame(payload);
            }
        }
        catch (Exception ex)
        {
            HelperLog.Write($"Presentation: pipe loop ended: {ex.Message}");
        }
        finally
        {
            HelperLog.Write("Presentation: closing.");
            try { _forms.FirstOrDefault()?.BeginInvoke(context.ExitThread); } catch { context.ExitThread(); }
        }
    }

    private void ShowFrame(byte[] jpeg)
    {
        Bitmap? bitmap;
        try { bitmap = new Bitmap(new MemoryStream(jpeg)); }
        catch (Exception ex)
        {
            HelperLog.Write($"Presentation: bad frame: {ex.Message}");
            return;
        }

        try
        {
            _forms[0].BeginInvoke(() =>
            {
                var index = 0;
                foreach (var box in _boxes)
                {
                    var old = box.Image;
                    // Each monitor gets its own Bitmap copy so disposing one never breaks another.
                    box.Image = index++ == 0 ? bitmap : new Bitmap(bitmap);
                    old?.Dispose();
                    if (_forms[index - 1].Tag is Label label && label.Visible)
                        label.Visible = false;
                }
            });
        }
        catch (Exception ex)
        {
            bitmap.Dispose();
            HelperLog.Write($"Presentation: could not show frame: {ex.Message}");
        }
    }

    private static bool ReadExactly(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n == 0)
                return false;
            read += n;
        }
        return true;
    }

    // ---- input lock -----------------------------------------------------------------------

    private void InstallHooks()
    {
        _keyboardProc = static (_, _, _) => 1;
        _mouseProc = static (_, _, _) => 1;
        var module = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        HelperLog.Write($"Presentation: input lock installed (keyboard={_keyboardHook != 0}, mouse={_mouseHook != 0}).");
    }

    private void RemoveHooks()
    {
        if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = 0;
    }

    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;

    private delegate nint LowLevelProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
