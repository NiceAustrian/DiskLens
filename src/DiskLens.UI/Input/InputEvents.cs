using SkiaSharp;

namespace DiskLens.UI.Input;

public enum PointerButton { None, Left, Right, Middle }

public enum CursorKind { Arrow, Hand, IBeam, ResizeHorizontal, ResizeVertical, Crosshair }

[Flags]
public enum Modifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Super = 8 }

/// <summary>Keys we care about. Mapped from the windowing layer's key codes.</summary>
public enum Key
{
    Unknown, Escape, Enter, Space, Tab, Backspace, Delete,
    Left, Right, Up, Down, Home, End, PageUp, PageDown,
    A, C, F, V, X, Z, Y, F5,
}

public sealed class PointerEvent
{
    public required SKPoint Position { get; init; }          // window coordinates (logical pixels)
    public PointerButton Button { get; init; }
    public Modifiers Modifiers { get; init; }
    public int ClickCount { get; init; } = 1;
    public SKPoint ScrollDelta { get; init; }                // lines; positive Y = scroll up
    /// <summary>True for finger input: no hover, larger targets, drag scrolls, long-press opens menus.</summary>
    public bool IsTouch { get; init; }
    public bool Handled { get; set; }
}

public sealed class KeyEvent
{
    public required Key Key { get; init; }
    public Modifiers Modifiers { get; init; }
    public bool IsRepeat { get; init; }
    public bool Handled { get; set; }
}

public sealed class TextInputEvent
{
    public required char Character { get; init; }
    public bool Handled { get; set; }
}
