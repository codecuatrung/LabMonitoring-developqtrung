using System.Runtime.Versioning;

namespace LabMonitoring.Agent.RemoteHelper;

/// <summary>
/// The "Đang được hỗ trợ từ xa" strip shown on the target machine for the duration of a session
/// (ADR-0013, confirmed with the user as a required v1 element). A borderless, topmost, non-taskbar
/// WinForms window — the simplest option since this codebase uses no UI framework anywhere else and
/// WinForms ships in the SDK for free. Runs its own message loop on a dedicated STA thread so it
/// doesn't block the session's async WebRTC/pipe work on the main thread.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class RemoteControlBanner
{
    private readonly object _lock = new();
    private Form? _form;
    private Thread? _thread;

    public void Show()
    {
        _thread = new Thread(RunMessageLoop) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void RunMessageLoop()
    {
        ApplicationConfiguration.Initialize();

        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 800, 24);
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            TopMost = true,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(bounds.Left, bounds.Top, bounds.Width, 26),
            BackColor = Color.FromArgb(196, 30, 30),
        };
        form.Controls.Add(new Label
        {
            Text = "Đang được hỗ trợ từ xa",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        });

        lock (_lock) { _form = form; }
        Application.Run(form);
    }

    public void Close()
    {
        Form? form;
        lock (_lock) { form = _form; }
        if (form is { IsHandleCreated: true, IsDisposed: false })
        {
            try { form.Invoke(form.Close); } catch (ObjectDisposedException) { /* already closing */ }
        }
    }
}
