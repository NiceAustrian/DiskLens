namespace DiskLens.Core.Platform;

/// <summary>
/// A window-procedure hook: return a result to swallow the message, or null to let it through.
/// Used by shell integrations that need to see menu messages (owner-drawn Explorer submenus).
/// </summary>
public delegate nint? NativeMessageFilter(nint hwnd, uint msg, nint wParam, nint lParam);

/// <summary>Enough of the host window for platform integrations: its handle and a way to hook messages.</summary>
public sealed record NativeWindow(nint Handle, Func<NativeMessageFilter, IDisposable>? AddMessageFilter);

/// <summary>An application item merged into the platform menu; null label = separator.</summary>
public readonly record struct NativeMenuItem(string? Label, bool IsEnabled = true)
{
    public static readonly NativeMenuItem Separator = new(null);
}

/// <summary>
/// The platform's own file context menu (Explorer's "Open with", "Properties", "Send to", shell
/// extensions...). Shown modally at a point in the window's client area.
/// </summary>
public interface INativeContextMenu
{
    bool IsSupported { get; }

    /// <summary>
    /// Shows the menu with <paramref name="customItems"/> on top and blocks until it closes.
    /// Coordinates are client pixels (not logical units). Returns the index of the chosen custom
    /// item, or -1 if the user picked a platform command or dismissed the menu.
    /// </summary>
    int Show(string path, NativeWindow window, int clientX, int clientY, IReadOnlyList<NativeMenuItem> customItems);
}

public sealed class NoNativeContextMenu : INativeContextMenu
{
    public bool IsSupported => false;
    public int Show(string path, NativeWindow window, int clientX, int clientY, IReadOnlyList<NativeMenuItem> customItems) => -1;
}
