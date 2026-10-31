using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LabMonitoring.Agent.RemoteHelper;

/// <summary>
/// Periodic GDI <c>BitBlt</c> capture of the full multi-monitor virtual screen into a 32bpp BGRA
/// buffer (ADR-0013 — v1 uses plain BitBlt, not the DXGI Desktop Duplication API; that's a later
/// latency optimization, not done now). Works here because this process runs inside the interactive
/// session (see <see cref="InputInjector"/>'s doc comment for why that matters).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ScreenCapture : IDisposable
{
    private readonly IntPtr _hdcScreen;
    private readonly IntPtr _hdcMem;
    private IntPtr _hBitmap;
    private IntPtr _bits;
    private int _width;
    private int _height;

    public ScreenCapture()
    {
        _hdcScreen = GetDC(IntPtr.Zero);
        _hdcMem = CreateCompatibleDC(_hdcScreen);
    }

    /// <summary>Captures one frame. Returns BGRA pixel bytes (top-down, 4 bytes/pixel) plus dimensions.</summary>
    public (byte[] Bgra, int Width, int Height) Capture()
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (width != _width || height != _height)
            Resize(width, height);

        var ok = BitBlt(_hdcMem, 0, 0, width, height, _hdcScreen, x, y, SRCCOPY);
        LastBitBltOk = ok;
        LastWin32Error = ok ? 0 : Marshal.GetLastWin32Error();
        LastHdcScreenValid = _hdcScreen != IntPtr.Zero;

        var buffer = new byte[width * height * 4];
        Marshal.Copy(_bits, buffer, 0, buffer.Length);
        return (buffer, width, height);
    }

    public bool LastBitBltOk { get; private set; }
    public int LastWin32Error { get; private set; }
    public bool LastHdcScreenValid { get; private set; }

    private void Resize(int width, int height)
    {
        if (_hBitmap != IntPtr.Zero)
        {
            DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }

        var info = new BITMAPINFO
        {
            biSize = 40, // sizeof(BITMAPINFOHEADER) — fixed by the Win32 ABI, excludes the trailing color table
            biWidth = width,
            biHeight = -height, // negative = top-down DIB, matches VideoPixelFormatsEnum.Bgra row order
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        _hBitmap = CreateDIBSection(_hdcMem, ref info, DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
        SelectObject(_hdcMem, _hBitmap);
        _width = width;
        _height = height;
    }

    public void Dispose()
    {
        if (_hBitmap != IntPtr.Zero) DeleteObject(_hBitmap);
        DeleteDC(_hdcMem);
        ReleaseDC(IntPtr.Zero, _hdcScreen);
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public uint biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        public uint bmiColors; // unused for 32bpp BI_RGB, present so BITMAPINFOHEADER is fully laid out
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
}
