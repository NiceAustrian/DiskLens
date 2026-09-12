using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Layout;
using DiskLens.UI.Rendering;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

/// <summary>
/// A full-window layer in the overlay that hosts a floating panel. Clicking outside the panel or
/// pressing Escape dismisses it. Base for menus and dialogs.
/// </summary>
public abstract class Popup : Element
{
    private readonly Tween _appear = new(0);

    protected Popup()
    {
        CanFocus = true;
    }

    public bool DimBackground { get; init; }
    public event Action? Closed;

    /// <summary>Adds the popup to the root's overlay and focuses it.</summary>
    public void Show(UiRoot root)
    {
        root.Overlay.Add(this);
        Focus();
        Animate(_appear.To(1, 0.15f, Easing.OutCubic));
    }

    public void Close()
    {
        if (Parent is null) return;
        Parent.Remove(this);
        Closed?.Invoke();
    }

    protected float Appear => _appear.Value;

    protected override void OnDraw(SKCanvas canvas)
    {
        if (DimBackground)
        {
            using var dim = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(0x70 * _appear.Value)) };
            canvas.DrawRect(Bounds, dim);
        }
    }

    protected internal override void OnPointerDown(PointerEvent e)
    {
        // Reached us directly: the click was outside every child panel.
        Close();
        e.Handled = true;
    }

    protected internal override void OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }
}

public sealed record MenuItem(string Label, Icon Icon, Action? Action, bool IsDanger = false, bool IsEnabled = true, string? Shortcut = null)
{
    public static readonly MenuItem Separator = new("", Icon.None, null);
    public bool IsSeparator => Label.Length == 0 && Action is null;
}

/// <summary>Right-click menu anchored at a point.</summary>
public sealed class ContextMenu : Popup
{
    private readonly IReadOnlyList<MenuItem> _items;
    private readonly SKPoint _anchor;
    private readonly List<SKRect> _rects = [];
    private int _hover = -1;
    private SKRect _panel;

    private const float ItemH = 30, SepH = 9, PadY = 6, MinW = 200;

    public ContextMenu(IReadOnlyList<MenuItem> items, SKPoint anchor)
    {
        _items = items;
        _anchor = anchor;
    }

    public static void Show(UiRoot root, SKPoint anchor, params MenuItem[] items) => new ContextMenu(items, anchor).Show(root);

    protected override void ArrangeContent(SKRect bounds)
    {
        var t = Theme;
        var w = MinW;
        foreach (var it in _items)
        {
            if (it.IsSeparator) continue;
            var tw = t.Body.Measure(it.Label) + 52 + (it.Shortcut is null ? 0 : t.SmallMuted.Measure(it.Shortcut) + 24);
            w = Math.Max(w, tw);
        }
        var h = PadY * 2 + _items.Sum(i => i.IsSeparator ? SepH : ItemH);
        var x = Math.Min(_anchor.X, bounds.Right - w - 8);
        var y = _anchor.Y + h > bounds.Bottom - 8 ? _anchor.Y - h : _anchor.Y;
        _panel = new SKRect(x, y, x + w, y + h);

        _rects.Clear();
        var cy = y + PadY;
        foreach (var it in _items)
        {
            var ih = it.IsSeparator ? SepH : ItemH;
            _rects.Add(new SKRect(x + 4, cy, x + w - 4, cy + ih));
            cy += ih;
        }
    }

    protected override void OnDrawOverlay(SKCanvas canvas)
    {
        var t = Theme;
        var a = Appear;
        var panel = _panel;
        panel.Offset(0, -4 * (1 - a));
        var rr = new SKRoundRect(panel, t.Radius);

        using var paint = new SKPaint { IsAntialias = true };
        paint.Color = t.Shadow.WithAlpha((byte)(t.Shadow.Alpha * a));
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10);
        var sh = panel; sh.Offset(0, 6);
        canvas.DrawRoundRect(new SKRoundRect(sh, t.Radius), paint);
        paint.MaskFilter = null;

        paint.Color = t.SurfaceRaised.WithAlpha((byte)(255 * a));
        canvas.DrawRoundRect(rr, paint);
        paint.Color = t.BorderStrong.WithAlpha((byte)(t.BorderStrong.Alpha * a));
        paint.IsStroke = true;
        canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(panel, -0.5f, -0.5f), t.Radius - 0.5f), paint);
        paint.IsStroke = false;

        for (var i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var r = _rects[i];
            r.Offset(0, -4 * (1 - a));
            if (it.IsSeparator)
            {
                paint.Color = t.Border;
                canvas.DrawRect(r.Left + 8, r.MidY, r.Width - 16, 1, paint);
                continue;
            }
            if (i == _hover && it.IsEnabled)
            {
                paint.Color = it.IsDanger ? t.Danger.WithAlpha(0x22) : t.SurfaceHover;
                canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(r, -2, -1), t.RadiusSmall), paint);
            }
            var fg = !it.IsEnabled ? t.TextMuted : it.IsDanger ? t.Danger : t.Text;
            Icons.Draw(canvas, it.Icon, r.Left + 20, r.MidY, 15, it.IsEnabled ? (it.IsDanger ? t.Danger : t.TextSecondary) : t.TextMuted, 1.5f);
            TextRender.Draw(canvas, it.Label, r.Left + 38, r.MidY, t.Body.With(fg.WithAlpha((byte)(fg.Alpha * a))));
            if (it.Shortcut is not null)
                TextRender.Draw(canvas, it.Shortcut, r.Right - 12, r.MidY, t.SmallMuted, TextAlign.Right);
        }
    }

    private int ItemAt(SKPoint p)
    {
        for (var i = 0; i < _rects.Count; i++)
            if (!_items[i].IsSeparator && _rects[i].Contains(p)) return i;
        return -1;
    }

    protected internal override void OnPointerMove(PointerEvent e)
    {
        var h = ItemAt(e.Position);
        if (h != _hover) { _hover = h; Invalidate(); }
        Cursor = h >= 0 && _items[h].IsEnabled ? CursorKind.Hand : CursorKind.Arrow;
        base.OnPointerMove(e);
    }

    protected internal override void OnPointerDown(PointerEvent e)
    {
        if (_panel.Contains(e.Position)) { e.Handled = true; return; }   // inside: wait for click
        base.OnPointerDown(e);
    }

    protected internal override void OnClick(PointerEvent e)
    {
        var i = ItemAt(e.Position);
        if (i >= 0 && _items[i].IsEnabled)
        {
            Close();
            _items[i].Action?.Invoke();
        }
        e.Handled = true;
    }
}

/// <summary>Modal yes/no dialog.</summary>
public sealed class ConfirmDialog : Popup
{
    private readonly Card _card;

    public ConfirmDialog(string title, string message, string confirmLabel, Action onConfirm, bool danger = false)
    {
        DimBackground = true;
        _card = new Card { FixedWidth = 440, Padding = new Thickness(22, 20) };
        var col = _card.Add(new Column { Gap = 10, CrossAlign = CrossAlign.Stretch });
        col.Add(new Label(title) { StyleSelector = t => t.Title });
        col.Add(new WrappedLabel(message));
        var buttons = col.Add(new Row { Gap = 8, MainAlign = MainAlign.End, Margin = new Thickness(0, 10, 0, 0) });
        var cancel = buttons.Add(new Button("Cancel"));
        cancel.Activated += Close;
        var ok = buttons.Add(new Button(confirmLabel) { Style = danger ? ButtonStyle.Danger : ButtonStyle.Primary });
        ok.Activated += () => { Close(); onConfirm(); };
        Add(_card);
    }

    protected override void ArrangeContent(SKRect bounds)
    {
        var size = _card.Measure(new SKSize(bounds.Width, bounds.Height));
        var x = bounds.MidX - size.Width / 2;
        var y = bounds.MidY - size.Height / 2 - 20;
        _card.Arrange(new SKRect(x, y, x + size.Width, y + size.Height));
    }

    protected override void OnDraw(SKCanvas canvas)
    {
        base.OnDraw(canvas);
        _card.Opacity = Appear;
    }

    protected internal override void OnPointerDown(PointerEvent e)
    {
        if (_card.Bounds.Contains(e.Position)) return;   // inside the card: let buttons handle it
        base.OnPointerDown(e);
    }

    protected internal override void OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; return; }   // avoid accidental confirms
        base.OnKeyDown(e);
    }
}

/// <summary>Multi-line label with simple word wrapping.</summary>
public sealed class WrappedLabel(string text) : Element
{
    private readonly List<string> _lines = [];
    private float _wrapWidth = -1;

    public Func<Theme, TextStyle> StyleSelector { get; set; } = t => t.BodySecondary;

    private void Wrap(float width)
    {
        if (Math.Abs(width - _wrapWidth) < 0.5f) return;
        _wrapWidth = width;
        _lines.Clear();
        var style = StyleSelector(Theme);
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (style.Measure(candidate) <= width || line.Length == 0) line = candidate;
                else { _lines.Add(line); line = word; }
            }
            _lines.Add(line);
        }
    }

    protected override SKSize MeasureContent(SKSize available)
    {
        Wrap(available.Width);
        return new SKSize(available.Width, _lines.Count * StyleSelector(Theme).LineHeight * 1.25f);
    }

    protected override void OnDraw(SKCanvas canvas)
    {
        Wrap(Bounds.Width);
        var style = StyleSelector(Theme);
        var lh = style.LineHeight * 1.25f;
        for (var i = 0; i < _lines.Count; i++)
            TextRender.Draw(canvas, _lines[i], Bounds.Left, Bounds.Top + lh * i + lh / 2, style);
    }
}
