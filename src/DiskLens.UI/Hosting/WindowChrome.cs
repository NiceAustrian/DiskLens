using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SkiaSharp;

namespace DiskLens.UI.Hosting;

/// <summary>Regions of a custom title bar as reported to the OS.</summary>
public enum CaptionHit { None, Client, Caption, Minimize, Maximize, Close }

/// <summary>
/// What the app sees of the window frame: whether the title bar is ours to draw, which caption
/// button is hovered/pressed (fed by the OS on Windows), and window commands.
/// </summary>
public interface IWindowChrome
{
    bool IsCustomFrame { get; }
    bool IsMaximized { get; }
    CaptionHit HoveredButton { get; }
    CaptionHit PressedButton { get; }

    /// <summary>Set by the app: maps a logical point to a caption region so the OS can drag/snap.</summary>
    Func<SKPoint, CaptionHit>? CaptionHitTest { get; set; }

    event Action? Changed;

    void Minimize();
    void ToggleMaximize();
    void Close();
    void SetDarkMode(bool dark);

    /// <summary>
    /// Hooks the native window procedure (Windows only). The filter returns a result to swallow the
    /// message or null to pass it on. Dispose the handle to unhook. No-op on other platforms.
    /// </summary>
    IDisposable AddMessageFilter(Func<nint, uint, nint, nint, nint?> filter);
}

/// <summary>Fallback for platforms where the native frame stays: nothing to do.</summary>
internal sealed class NativeChrome(Action minimize, Action toggleMaximize, Action close, Func<bool> isMaximized) : IWindowChrome
{
    public bool IsCustomFrame => false;
    public bool IsMaximized => isMaximized();
    public CaptionHit HoveredButton => CaptionHit.None;
    public CaptionHit PressedButton => CaptionHit.None;
    public Func<SKPoint, CaptionHit>? CaptionHitTest { get; set; }
    public event Action? Changed { add { } remove { } }
    public void Minimize() => minimize();
    public void ToggleMaximize() => toggleMaximize();
    public void Close() => close();
    public void SetDarkMode(bool dark) { }
    public IDisposable AddMessageFilter(Func<nint, uint, nint, nint, nint?> filter) => new NoopDisposable();

    private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
}

/// <summary>
/// Windows: removes the standard title bar but keeps the DWM frame (shadow, resize borders, snap,
/// Win+arrow) by subclassing the window procedure. The app draws the caption; the OS is told where
/// it is via WM_NCHITTEST so dragging, double-click-to-maximise and Snap Layouts keep working.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsChrome : IWindowChrome
{
    private readonly nint _hwnd;
    private readonly Func<float> _scale;
    private readonly WndProc _proc;         // keep the delegate alive
    private readonly nint _originalProc;
    private CaptionHit _hovered, _pressed;
    private bool _tracking;
    private readonly List<Func<nint, uint, nint, nint, nint?>> _filters = [];

    public WindowsChrome(nint hwnd, Func<float> scale)
    {
        _hwnd = hwnd;
        _scale = scale;
        _proc = HandleMessage;
        _originalProc = SetWindowLongPtrW(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
        // Re-evaluate the frame with our WM_NCCALCSIZE in place.
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public bool IsCustomFrame => true;
    public bool IsMaximized => IsZoomed(_hwnd);
    public CaptionHit HoveredButton => _hovered;
    public CaptionHit PressedButton => _pressed;
    public Func<SKPoint, CaptionHit>? CaptionHitTest { get; set; }
    public event Action? Changed;

    public void Minimize() => ShowWindow(_hwnd, SW_MINIMIZE);
    public void ToggleMaximize() => ShowWindow(_hwnd, IsMaximized ? SW_RESTORE : SW_MAXIMIZE);
    public void Close() => PostMessageW(_hwnd, WM_CLOSE, 0, 0);

    public void SetDarkMode(bool dark)
    {
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    public IDisposable AddMessageFilter(Func<nint, uint, nint, nint, nint?> filter)
    {
        _filters.Add(filter);
        return new FilterHandle(this, filter);
    }

    private sealed class FilterHandle(WindowsChrome owner, Func<nint, uint, nint, nint, nint?> filter) : IDisposable
    {
        public void Dispose() => owner._filters.Remove(filter);
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        for (var i = _filters.Count - 1; i >= 0; i--)
        {
            if (_filters[i](hwnd, msg, wParam, lParam) is { } handled) return handled;
        }
        switch (msg)
        {
            case WM_NCCALCSIZE when wParam != 0:
            {
                // Let Windows compute the client area (it removes the whole frame), then give the top
                // back so the caption disappears while the side/bottom resize borders stay.
                var top = Marshal.ReadInt32(lParam, 4);   // NCCALCSIZE_PARAMS.rgrc[0].top
                var result = CallWindowProcW(_originalProc, hwnd, msg, wParam, lParam);
                if (IsZoomed(hwnd))
                {
                    // Maximised windows extend past the screen edge by the frame width – inset the top.
                    var dpi = GetDpiForWindow(hwnd);
                    top += GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
                }
                Marshal.WriteInt32(lParam, 4, top);
                return result;
            }
            case WM_NCHITTEST:
            {
                var result = CallWindowProcW(_originalProc, hwnd, msg, wParam, lParam);
                if (result != HTCLIENT) return result;
                var (x, y) = ClientPoint(lParam);
                var scale = _scale();
                var border = (int)(6 * scale);
                if (!IsZoomed(hwnd) && y < border)
                {
                    GetClientRect(hwnd, out var rc);
                    if (x < border) return HTTOPLEFT;
                    if (x > rc.Right - border) return HTTOPRIGHT;
                    return HTTOP;
                }
                return CaptionHitTest?.Invoke(new SKPoint(x / scale, y / scale)) switch
                {
                    CaptionHit.Caption => HTCAPTION,
                    CaptionHit.Minimize => HTMINBUTTON,
                    CaptionHit.Maximize => HTMAXBUTTON,
                    CaptionHit.Close => HTCLOSE,
                    _ => HTCLIENT,
                };
            }
            case WM_NCMOUSEMOVE:
            {
                SetHover(FromHitCode(wParam));
                if (!_tracking)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE | TME_NONCLIENT, hwndTrack = hwnd };
                    _tracking = TrackMouseEvent(ref tme);
                }
                if (_hovered != CaptionHit.None) return 0;
                break;
            }
            case WM_NCMOUSELEAVE:
                _tracking = false;
                SetHover(CaptionHit.None);
                break;
            case WM_MOUSEMOVE:
                SetHover(CaptionHit.None);
                break;
            case WM_NCLBUTTONDOWN:
            {
                var hit = FromHitCode(wParam);
                if (hit is CaptionHit.Minimize or CaptionHit.Maximize or CaptionHit.Close)
                {
                    _pressed = hit;
                    Changed?.Invoke();
                    return 0;   // swallow: DefWindowProc would otherwise act on its own (invisible) buttons
                }
                break;
            }
            case WM_NCLBUTTONUP:
            {
                var hit = FromHitCode(wParam);
                var pressed = _pressed;
                _pressed = CaptionHit.None;
                Changed?.Invoke();
                if (pressed != CaptionHit.None)
                {
                    if (pressed == hit)
                    {
                        switch (hit)
                        {
                            case CaptionHit.Minimize: Minimize(); break;
                            case CaptionHit.Maximize: ToggleMaximize(); break;
                            case CaptionHit.Close: Close(); break;
                        }
                    }
                    return 0;
                }
                break;
            }
            case WM_NCLBUTTONDBLCLK when FromHitCode(wParam) is CaptionHit.Minimize or CaptionHit.Maximize or CaptionHit.Close:
                return 0;
            case WM_SIZE:
                Changed?.Invoke();
                break;
        }
        return CallWindowProcW(_originalProc, hwnd, msg, wParam, lParam);
    }

    private void SetHover(CaptionHit hit)
    {
        if (_hovered == hit) return;
        _hovered = hit;
        Changed?.Invoke();
    }

    private (int X, int Y) ClientPoint(nint lParam)
    {
        var pt = new POINT { X = (short)(lParam & 0xFFFF), Y = (short)((lParam >> 16) & 0xFFFF) };
        ScreenToClient(_hwnd, ref pt);
        return (pt.X, pt.Y);
    }

    private static CaptionHit FromHitCode(nint code) => (int)code switch
    {
        HTMINBUTTON => CaptionHit.Minimize,
        HTMAXBUTTON => CaptionHit.Maximize,
        HTCLOSE => CaptionHit.Close,
        HTCAPTION => CaptionHit.Caption,
        _ => CaptionHit.None,
    };

    // Win32 ----------------------------------------------------------------------------------------
    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_SIZE = 0x0005, WM_CLOSE = 0x0010, WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084,
        WM_MOUSEMOVE = 0x0200, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2,
        WM_NCLBUTTONDBLCLK = 0x00A3, WM_NCMOUSELEAVE = 0x02A2;
    private const int HTCLIENT = 1, HTCAPTION = 2, HTMINBUTTON = 8, HTMAXBUTTON = 9, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTCLOSE = 20;
    private const int SM_CXPADDEDBORDER = 92, SM_CYSIZEFRAME = 33;
    private const int SW_MAXIMIZE = 3, SW_MINIMIZE = 6, SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;
    private const uint TME_LEAVE = 0x00000002, TME_NONCLIENT = 0x00000010;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct TRACKMOUSEEVENT { public uint cbSize; public uint dwFlags; public nint hwndTrack; public uint dwHoverTime; }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern nint CallWindowProcW(nint prev, nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref POINT pt);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out RECT rc);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
