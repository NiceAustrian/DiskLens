using DiskLens.UI.Animation;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using SkiaSharp;

namespace DiskLens.UI.Elements;

/// <summary>
/// Base of the retained element tree. Layout is two-pass (measure, arrange); bounds are stored in
/// absolute window coordinates so hit-testing and drawing never need transforms – scrolling and
/// panning re-arrange their children instead.
/// </summary>
public abstract class Element
{
    private readonly List<Element> _children = [];
    private List<IAnimation>? _pendingAnimations;
    private bool _isVisible = true;

    public Element? Parent { get; private set; }
    public IReadOnlyList<Element> Children => _children;

    /// <summary>Absolute bounds after the last arrange pass.</summary>
    public SKRect Bounds { get; protected set; }
    public float Width => Bounds.Width;
    public float Height => Bounds.Height;

    /// <summary>Layout hints consumed by the parent container.</summary>
    public float? FixedWidth { get; set; }
    public float? FixedHeight { get; set; }
    public float MinWidth { get; set; }
    public float MinHeight { get; set; }
    public float MaxWidth { get; set; } = float.PositiveInfinity;
    public float MaxHeight { get; set; } = float.PositiveInfinity;
    /// <summary>Share of the leftover space in a Row/Column. 0 = size to content.</summary>
    public float Flex { get; set; }
    public Thickness Margin { get; set; }

    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; InvalidateLayout(); } }
    }

    public bool IsHitTestVisible { get; set; } = true;
    public bool ClipsChildren { get; set; }
    public float Opacity { get; set; } = 1;
    public CursorKind Cursor { get; set; } = CursorKind.Arrow;
    public string? Tooltip { get; set; }
    public object? Tag { get; set; }

    public bool IsHovered { get; internal set; }
    public bool IsPressed { get; internal set; }
    public bool IsFocused { get; internal set; }
    public bool CanFocus { get; set; }

    public UiRoot? Root => this as UiRoot ?? Parent?.Root;
    public Theme Theme => Root?.Theme ?? Theme.Dark;

    // Tree ---------------------------------------------------------------------------------------
    public T Add<T>(T child) where T : Element
    {
        if (child.Parent is not null) child.Parent.Remove(child);
        child.Parent = this;
        _children.Add(child);
        if (Root is { } root) child.OnAttached(root);
        InvalidateLayout();
        return child;
    }

    public void Insert(int index, Element child)
    {
        if (child.Parent is not null) child.Parent.Remove(child);
        child.Parent = this;
        _children.Insert(index, child);
        if (Root is { } root) child.OnAttached(root);
        InvalidateLayout();
    }

    /// <summary>Called once the element (and its subtree) is reachable from a root. Flushes queued animations.</summary>
    private void OnAttached(UiRoot root)
    {
        if (_pendingAnimations is { } pending)
        {
            _pendingAnimations = null;
            foreach (var a in pending) root.Animator.Run(a, this);
            root.RequestRedraw();
        }
        foreach (var c in _children) c.OnAttached(root);
    }

    public void Remove(Element child)
    {
        if (_children.Remove(child))
        {
            Root?.OnElementRemoved(child);
            child.Parent = null;
            InvalidateLayout();
        }
    }

    public void ClearChildren()
    {
        foreach (var c in _children)
        {
            Root?.OnElementRemoved(c);
            c.Parent = null;
        }
        _children.Clear();
        InvalidateLayout();
    }

    public IEnumerable<Element> Ancestors()
    {
        for (var e = Parent; e is not null; e = e.Parent) yield return e;
    }

    public bool IsAncestorOf(Element other) => other.Ancestors().Contains(this);

    // Invalidation -------------------------------------------------------------------------------
    /// <summary>Repaints this element's area (plus a margin for shadows/glows) on the next frame.</summary>
    public void Invalidate()
    {
        if (Root is not { } root) return;
        if (Bounds.IsEmpty) root.RequestRedraw();
        else root.RequestRedraw(SKRect.Inflate(Bounds, RedrawMargin, RedrawMargin));
    }

    public void InvalidateLayout() => Root?.RequestLayout();

    /// <summary>How far outside its bounds an element may paint (drop shadows, focus glows).</summary>
    public const float RedrawMargin = 24;

    /// <summary>Runs an animation, repainting this element after every tick. Queued if not yet attached to a root.</summary>
    public void Animate(IAnimation animation)
    {
        if (Root is { } root) root.Animator.Run(animation, this);
        else (_pendingAnimations ??= []).Add(animation);
    }

    // Layout -------------------------------------------------------------------------------------
    /// <summary>Returns the desired size within <paramref name="available"/>, honouring fixed/min/max hints.</summary>
    public SKSize Measure(SKSize available)
    {
        if (!IsVisible) return SKSize.Empty;
        var inner = new SKSize(
            Math.Max(0, (FixedWidth ?? available.Width) - Margin.Horizontal),
            Math.Max(0, (FixedHeight ?? available.Height) - Margin.Vertical));
        var desired = MeasureContent(inner);
        var w = FixedWidth ?? Math.Clamp(desired.Width, MinWidth, MaxWidth);
        var h = FixedHeight ?? Math.Clamp(desired.Height, MinHeight, MaxHeight);
        return new SKSize(w + Margin.Horizontal, h + Margin.Vertical);
    }

    /// <summary>Positions the element at <paramref name="rect"/> (absolute) and lays out children.</summary>
    public void Arrange(SKRect rect)
    {
        Bounds = new SKRect(rect.Left + Margin.Left, rect.Top + Margin.Top, rect.Right - Margin.Right, rect.Bottom - Margin.Bottom);
        if (Bounds.Width < 0) Bounds = new SKRect(Bounds.Left, Bounds.Top, Bounds.Left, Bounds.Bottom);
        if (Bounds.Height < 0) Bounds = new SKRect(Bounds.Left, Bounds.Top, Bounds.Right, Bounds.Top);
        if (IsVisible) ArrangeContent(Bounds);
    }

    protected virtual SKSize MeasureContent(SKSize available)
    {
        // Default: the union of children measured against the full available size.
        float w = 0, h = 0;
        foreach (var c in _children)
        {
            var s = c.Measure(available);
            w = Math.Max(w, s.Width);
            h = Math.Max(h, s.Height);
        }
        return new SKSize(w, h);
    }

    protected virtual void ArrangeContent(SKRect bounds)
    {
        foreach (var c in _children) c.Arrange(bounds);
    }

    // Drawing ------------------------------------------------------------------------------------
    public void Draw(SKCanvas canvas)
    {
        if (!IsVisible || Opacity <= 0) return;
        var restore = canvas.Save();
        if (ClipsChildren) canvas.ClipRect(Bounds, antialias: true);
        SKPaint? layer = null;
        if (Opacity < 1) canvas.SaveLayer(layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(Opacity * 255)) });
        OnDraw(canvas);
        DrawChildren(canvas);
        OnDrawOverlay(canvas);
        canvas.RestoreToCount(restore);
        layer?.Dispose();
    }

    protected virtual void DrawChildren(SKCanvas canvas)
    {
        foreach (var c in _children)
        {
            // Children may paint slightly outside their bounds (shadows) – reject generously.
            if (canvas.QuickReject(SKRect.Inflate(c.Bounds, RedrawMargin, RedrawMargin))) continue;
            c.Draw(canvas);
        }
    }

    /// <summary>Draws this element's own visuals, underneath its children.</summary>
    protected virtual void OnDraw(SKCanvas canvas) { }

    /// <summary>Draws on top of children (selection rings, overlays).</summary>
    protected virtual void OnDrawOverlay(SKCanvas canvas) { }

    // Hit testing --------------------------------------------------------------------------------
    /// <summary>Deepest visible, hit-testable element at <paramref name="p"/>, or null.</summary>
    public virtual Element? HitTest(SKPoint p)
    {
        if (!IsVisible || !IsHitTestVisible || !Bounds.Contains(p)) return null;
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            var hit = _children[i].HitTest(p);
            if (hit is not null) return hit;
        }
        return this;
    }

    // Input hooks ---------------------------------------------------------------------------------
    // Events bubble from the hit element up to the root until one marks them Handled.
    public event Action<PointerEvent>? PointerDown;
    public event Action<PointerEvent>? PointerUp;
    public event Action<PointerEvent>? Clicked;
    public event Action<PointerEvent>? DoubleClicked;
    public event Action<PointerEvent>? PointerMoved;
    public event Action<PointerEvent>? Wheel;
    public event Action? PointerEntered;
    public event Action? PointerExited;
    public event Action<KeyEvent>? KeyDown;
    public event Action<TextInputEvent>? TextInput;

    protected internal virtual void OnPointerDown(PointerEvent e) => PointerDown?.Invoke(e);
    protected internal virtual void OnPointerUp(PointerEvent e) => PointerUp?.Invoke(e);
    protected internal virtual void OnClick(PointerEvent e) => Clicked?.Invoke(e);
    protected internal virtual void OnDoubleClick(PointerEvent e) => DoubleClicked?.Invoke(e);
    protected internal virtual void OnPointerMove(PointerEvent e) => PointerMoved?.Invoke(e);
    protected internal virtual void OnWheel(PointerEvent e) => Wheel?.Invoke(e);
    protected internal virtual void OnPointerEnter() { PointerEntered?.Invoke(); Invalidate(); }
    protected internal virtual void OnPointerExit() { PointerExited?.Invoke(); Invalidate(); }
    protected internal virtual void OnKeyDown(KeyEvent e) => KeyDown?.Invoke(e);
    protected internal virtual void OnTextInput(TextInputEvent e) => TextInput?.Invoke(e);
    protected internal virtual void OnFocusChanged(bool focused) => Invalidate();

    public void Focus() => Root?.SetFocus(this);
}

public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    public Thickness(float all) : this(all, all, all, all) { }
    public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
    public static readonly Thickness Zero = new(0);
    public static implicit operator Thickness(float all) => new(all);
}
