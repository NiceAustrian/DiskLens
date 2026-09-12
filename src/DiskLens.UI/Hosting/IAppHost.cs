namespace DiskLens.UI.Hosting;

/// <summary>
/// What the presentation layer needs from whatever hosts the <see cref="Elements.UiRoot"/> – a
/// desktop window, an Android view, ... Everything platform-specific stays behind this.
/// </summary>
public interface IAppHost
{
    /// <summary>Runs <paramref name="action"/> on the UI thread and wakes the render loop.</summary>
    void Post(Action action);

    /// <summary>Logical-to-device pixel ratio.</summary>
    float Scale { get; }

    /// <summary>Frame integration (custom title bar, message hooks). Valid after <see cref="Loaded"/>.</summary>
    IWindowChrome Chrome { get; }

    /// <summary>Native window handle (HWND on Windows), or 0 where that means nothing.</summary>
    nint NativeHandle { get; }

    /// <summary>Whether this host is driven by touch: bigger targets, drag-to-scroll, long-press menus.</summary>
    bool IsTouch { get; }

    /// <summary>Raised once the host window and its chrome exist.</summary>
    event Action? Loaded;

    /// <summary>Closes the application window / finishes the activity.</summary>
    void Close();
}
