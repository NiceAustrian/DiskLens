using System.Diagnostics;
using DiskLens.UI.Animation;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using SkiaSharp;

namespace DiskLens.UI.Elements;

/// <summary>
/// Top of the element tree. Owns the theme, the animator, input state (hover, capture, focus) and
/// the dirty flags. Knows nothing about windows – a host feeds it events and asks it to draw.
/// </summary>
public sealed class UiRoot : Element
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTick;
    private bool _needsLayout = true;
    private bool _needsRedraw = true;      // full repaint
    private SKRect _dirty = SKRect.Empty;  // partial repaint region (when _needsRedraw is false)
    private double _wakeupAt = double.PositiveInfinity;

    private Element? _hovered;
    private Element? _pressed;
    private Element? _captured;
    private Element? _focused;
    private SKPoint _pointer;
    private PointerButton _pressedButton;
    private bool _isTouch;
    private bool _suppressClick;
    private double _lastClickTime;
    private SKPoint _lastClickPos;
    private int _clickCount;
    private double _hoverSince;
    private string? _tooltipText;
    private SKPoint _tooltipAnchor;

    public UiRoot(Theme theme)
    {
        Theme = theme;
        Overlay = Add(new Layer { IsHitTestVisible = true });
        // The overlay is drawn last, so it is inserted last – content added later must go before it.
    }

    public new Theme Theme { get; private set; }
    public Animator Animator { get; } = new();
    public SKSize Size { get; private set; }
    public float Scale { get; private set; } = 1;

    /// <summary>Popups, menus and drag visuals live here, above all content.</summary>
    public Layer Overlay { get; }

    /// <summary>Set by the host; null when the platform offers no clipboard.</summary>
    public Widgets.IClipboard? Clipboard { get; set; }

    public Element? Focused => _focused;
    public SKPoint PointerPosition => _pointer;
    public CursorKind CurrentCursor => _captured?.Cursor ?? _hovered?.Cursor ?? CursorKind.Arrow;
    public bool NeedsRedraw => _needsRedraw || !_dirty.IsEmpty || Animator.HasWork;

    /// <summary>Seconds until the root wants to be ticked again without input (tooltip delay, caret blink), or null.</summary>
    public double? NextWakeupIn => double.IsPositiveInfinity(_wakeupAt) ? null : Math.Max(0, _wakeupAt - _clock.Elapsed.TotalSeconds);

    /// <summary>Asks the host to tick again after <paramref name="seconds"/> even if no input arrives.</summary>
    public void RequestWakeup(double seconds) => _wakeupAt = Math.Min(_wakeupAt, _clock.Elapsed.TotalSeconds + seconds);

    public event Action<CursorKind>? CursorChanged;

    /// <summary>Puts <paramref name="content"/> under the overlay layer, replacing previous content.</summary>
    public void SetContent(Element content)
    {
        foreach (var c in Children.Where(c => c != Overlay).ToList()) Remove(c);
        Insert(0, content);
    }

    public void SetTheme(Theme theme)
    {
        Theme = theme;
        RequestLayout();
    }

    public void Resize(SKSize size, float scale)
    {
        Size = size;
        Scale = scale;
        RequestLayout();
    }

    public void RequestLayout() { _needsLayout = true; _needsRedraw = true; }
    public void RequestRedraw() => _needsRedraw = true;

    /// <summary>Marks a window-space region for repaint. Regions accumulate into one bounding box.</summary>
    public void RequestRedraw(SKRect region)
    {
        if (_needsRedraw) return;
        _dirty = _dirty.IsEmpty ? region : SKRect.Union(_dirty, region);
    }

    // Frame ----------------------------------------------------------------------------------------
    /// <summary>Advances animations and lays out if needed. Returns true if a redraw is required.</summary>
    public bool Tick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Min(0.1, now - _lastTick);
        _lastTick = now;

        if (Animator.Tick(dt)) _needsRedraw = true;
        if (now >= _wakeupAt) _wakeupAt = double.PositiveInfinity;

        // Tooltip after a short hover delay
        if (_hovered?.Tooltip is { } tip && _tooltipText is null)
        {
            if (now - _hoverSince > 0.6)
            {
                _tooltipText = tip;
                _tooltipAnchor = _pointer;
                _needsRedraw = true;
            }
            else RequestWakeup(0.6 - (now - _hoverSince));
        }

        if (_needsLayout)
        {
            _needsLayout = false;
            Arrange(new SKRect(0, 0, Size.Width, Size.Height));
            // Layout can move things under the pointer.
            UpdateHover();
            _needsRedraw = true;
        }
        return _needsRedraw || !_dirty.IsEmpty;
    }

    /// <summary>
    /// Paints into <paramref name="canvas"/>, which must retain its contents between frames (the
    /// host keeps an offscreen scene surface). Only the dirty region is cleared and redrawn unless a
    /// full repaint was requested. Returns the region that was painted.
    /// </summary>
    public SKRect Render(SKCanvas canvas)
    {
        var region = _needsRedraw ? new SKRect(0, 0, Size.Width, Size.Height) : _dirty;
        _needsRedraw = false;
        _dirty = SKRect.Empty;

        canvas.Save();
        canvas.ClipRect(region);
        canvas.Clear(Theme.Background);
        Draw(canvas);
        DrawTooltip(canvas);
        canvas.Restore();
        return region;
    }

    protected override SKSize MeasureContent(SKSize available) => Size;

    protected override void ArrangeContent(SKRect bounds)
    {
        foreach (var c in Children) c.Arrange(bounds);
    }

    private void DrawTooltip(SKCanvas canvas)
    {
        if (_tooltipText is null) return;
        var style = Theme.Small.With(Theme.Text);
        const float padX = 10, padY = 6;
        var w = style.Measure(_tooltipText) + padX * 2;
        var h = style.LineHeight + padY * 2;
        var x = Math.Clamp(_tooltipAnchor.X + 12, 4, Size.Width - w - 4);
        var y = _tooltipAnchor.Y + 20;
        if (y + h > Size.Height - 4) y = _tooltipAnchor.Y - h - 8;
        var rect = new SKRoundRect(new SKRect(x, y, x + w, y + h), Theme.RadiusSmall);
        using var shadow = new SKPaint { Color = Theme.Shadow, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6), IsAntialias = true };
        canvas.DrawRoundRect(rect, shadow);
        using var fill = new SKPaint { Color = Theme.SurfaceRaised, IsAntialias = true };
        canvas.DrawRoundRect(rect, fill);
        using var border = new SKPaint { Color = Theme.BorderStrong, IsAntialias = true, IsStroke = true, StrokeWidth = 1 };
        canvas.DrawRoundRect(rect, border);
        TextRender.Draw(canvas, _tooltipText, x + padX, y + h / 2, style);
    }

    // Input dispatch -------------------------------------------------------------------------------
    public void DispatchPointerMove(SKPoint position, Modifiers modifiers)
    {
        _pointer = position;
        if (_captured is not null)
        {
            Bubble(_captured, e => e.OnPointerMove, new PointerEvent { Position = position, Button = _pressedButton, Modifiers = modifiers, IsTouch = _isTouch });
            return;
        }
        UpdateHover();
        if (_hovered is not null)
            Bubble(_hovered, e => e.OnPointerMove, new PointerEvent { Position = position, Modifiers = modifiers, IsTouch = _isTouch });
    }

    public void DispatchPointerDown(SKPoint position, PointerButton button, Modifiers modifiers, bool isTouch = false)
    {
        _pointer = position;
        _isTouch = isTouch;
        _suppressClick = false;
        HideTooltip();
        UpdateHover();
        var target = _hovered;
        if (target is null) return;

        var now = _clock.Elapsed.TotalSeconds;
        var isDouble = now - _lastClickTime < 0.4 && SKPoint.Distance(position, _lastClickPos) < (isTouch ? 32 : 6) && button == PointerButton.Left;
        _clickCount = isDouble ? _clickCount + 1 : 1;
        _lastClickTime = now;
        _lastClickPos = position;

        _pressed = target;
        _pressedButton = button;
        _captured = target;
        SetPressedVisual(target, true);

        // Focus follows clicks.
        var focusTarget = target;
        while (focusTarget is not null && !focusTarget.CanFocus) focusTarget = focusTarget.Parent;
        SetFocus(focusTarget);

        Bubble(target, e => e.OnPointerDown, new PointerEvent { Position = position, Button = button, Modifiers = modifiers, ClickCount = _clickCount, IsTouch = isTouch });
    }

    public void DispatchPointerUp(SKPoint position, PointerButton button, Modifiers modifiers, bool cancelled = false)
    {
        _pointer = position;
        var target = _captured ?? _hovered;
        _captured = null;
        if (target is null) return;

        var wasPressed = _pressed;
        _pressed = null;
        SetPressedVisual(wasPressed, false);

        Bubble(target, e => e.OnPointerUp, new PointerEvent { Position = position, Button = button, Modifiers = modifiers, IsTouch = _isTouch });

        // A click is press + release on the same element – unless a drag or long-press took over.
        if (cancelled || _suppressClick) { _suppressClick = false; UpdateHover(); return; }
        var under = HitTest(position);
        if (wasPressed is not null && under is not null && (under == wasPressed || wasPressed.IsAncestorOf(under)))
        {
            var ev = new PointerEvent { Position = position, Button = button, Modifiers = modifiers, ClickCount = _clickCount, IsTouch = _isTouch };
            if (_clickCount >= 2 && button == PointerButton.Left)
            {
                Bubble(wasPressed, e => e.OnDoubleClick, ev);
                if (!ev.Handled) Bubble(wasPressed, e => e.OnClick, ev);
            }
            else
            {
                Bubble(wasPressed, e => e.OnClick, ev);
            }
        }
        UpdateHover();
    }

    /// <summary>Touch long-press: delivered as a right-click to the pressed element; the following release is not a click.</summary>
    public void DispatchLongPress(SKPoint position)
    {
        var target = _pressed ?? HitTest(position);
        if (target is null) return;
        _suppressClick = true;
        SetPressedVisual(_pressed, false);
        _pressed = null;
        Bubble(target, e => e.OnClick, new PointerEvent { Position = position, Button = PointerButton.Right, IsTouch = true });
    }

    /// <summary>
    /// Lets a container (a scroll view starting a drag) take the pointer away from the pressed child:
    /// the child's press visual is cleared and the eventual release will not count as a click.
    /// </summary>
    public void TakeOverPointer(Element element)
    {
        _suppressClick = true;
        SetPressedVisual(_pressed, false);
        _pressed = null;
        _captured = element;
    }

    public void DispatchPointerWheel(SKPoint position, SKPoint delta, Modifiers modifiers)
    {
        _pointer = position;
        HideTooltip();
        UpdateHover();
        if (_hovered is not null)
            Bubble(_hovered, e => e.OnWheel, new PointerEvent { Position = position, ScrollDelta = delta, Modifiers = modifiers });
    }

    public void DispatchPointerLeave()
    {
        if (_captured is not null) return;
        SetHovered(null);
    }

    public void DispatchKeyDown(Key key, Modifiers modifiers, bool repeat)
    {
        var ev = new KeyEvent { Key = key, Modifiers = modifiers, IsRepeat = repeat };
        var target = _focused ?? (Element)this;
        for (var e = target; e is not null && !ev.Handled; e = e.Parent) e.OnKeyDown(ev);
    }

    public void DispatchTextInput(char c)
    {
        if (_focused is null) return;
        var ev = new TextInputEvent { Character = c };
        for (var e = _focused; e is not null && !ev.Handled; e = e.Parent) e.OnTextInput(ev);
    }

    public void SetFocus(Element? element)
    {
        if (_focused == element) return;
        var old = _focused;
        _focused = element;
        if (old is not null) { old.IsFocused = false; old.OnFocusChanged(false); }
        if (element is not null) { element.IsFocused = true; element.OnFocusChanged(true); }
    }

    /// <summary>Routes all pointer input to <paramref name="element"/> until the button is released.</summary>
    public void CapturePointer(Element element) => _captured = element;

    internal void OnElementRemoved(Element element)
    {
        if (_hovered is not null && (_hovered == element || element.IsAncestorOf(_hovered))) SetHovered(null);
        if (_pressed is not null && (_pressed == element || element.IsAncestorOf(_pressed))) _pressed = null;
        if (_captured is not null && (_captured == element || element.IsAncestorOf(_captured))) _captured = null;
        if (_focused is not null && (_focused == element || element.IsAncestorOf(_focused))) SetFocus(null);
    }

    private void UpdateHover() => SetHovered(HitTest(_pointer));

    private void SetHovered(Element? element)
    {
        if (_hovered == element) return;
        var oldCursor = CurrentCursor;
        var old = _hovered;
        _hovered = element;

        // Enter/leave fire on every element along the changed part of the ancestor chain.
        var oldChain = old is null ? [] : new HashSet<Element>(old.Ancestors().Prepend(old));
        var newChain = element is null ? [] : new HashSet<Element>(element.Ancestors().Prepend(element));
        foreach (var e in oldChain.Where(e => !newChain.Contains(e))) { e.IsHovered = false; e.OnPointerExit(); }
        foreach (var e in newChain.Where(e => !oldChain.Contains(e))) { e.IsHovered = true; e.OnPointerEnter(); }

        HideTooltip();
        _hoverSince = _clock.Elapsed.TotalSeconds;
        var newCursor = CurrentCursor;
        if (newCursor != oldCursor) CursorChanged?.Invoke(newCursor);
        RequestRedraw();
    }

    private void HideTooltip()
    {
        if (_tooltipText is null) return;
        _tooltipText = null;
        RequestRedraw();
    }

    private static void SetPressedVisual(Element? e, bool pressed)
    {
        if (e is null) return;
        e.IsPressed = pressed;
        e.Invalidate();
    }

    private static void Bubble(Element target, Func<Element, Action<PointerEvent>> handler, PointerEvent e)
    {
        for (var el = target; el is not null && !e.Handled; el = el.Parent) handler(el)(e);
    }
}

/// <summary>A transparent container that only exists to group or overlay children.</summary>
public class Layer : Element
{
    public override Element? HitTest(SKPoint p)
    {
        // Transparent to hits on itself: only children can be hit.
        if (!IsVisible || !IsHitTestVisible) return null;
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            var hit = Children[i].HitTest(p);
            if (hit is not null) return hit;
        }
        return null;
    }
}
