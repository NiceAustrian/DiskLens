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
public sealed class NativeChrome(Action minimize, Action toggleMaximize, Action close, Func<bool> isMaximized) : IWindowChrome
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
