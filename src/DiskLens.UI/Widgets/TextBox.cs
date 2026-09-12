using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

/// <summary>Single-line text input with caret, basic editing keys, clipboard paste and a placeholder.</summary>
public sealed class TextBox : Element
{
    private string _text = "";
    private int _caret;
    private float _scrollX;
    private double _blinkStart;

    public TextBox()
    {
        CanFocus = true;
        Cursor = CursorKind.IBeam;
        ClipsChildren = true;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            _caret = Math.Min(_caret, _text.Length);
            Invalidate();
        }
    }

    public string Placeholder { get; set; } = "";
    public Icon Icon { get; set; } = Icon.None;
    public Thickness Padding { get; set; } = new(12, 8);
    public bool UseMonospace { get; set; }

    public event Action<string>? TextChanged;
    public event Action<string>? Submitted;

    private TextStyle Style => (UseMonospace ? Theme.Mono : Theme.Body).With(Theme.Text);

    protected override SKSize MeasureContent(SKSize available) =>
        new(available.Width, Style.LineHeight + Padding.Vertical + 4);

    protected override void OnDraw(SKCanvas canvas)
    {
        var rect = Bounds;
        var rr = new SKRoundRect(rect, Theme.RadiusSmall + 2);
        using var paint = new SKPaint { IsAntialias = true, Color = Theme.Surface };
        canvas.DrawRoundRect(rr, paint);
        paint.Color = IsFocused ? Theme.Accent.WithAlpha(0xA0) : IsHovered ? Theme.BorderStrong : Theme.Border;
        paint.IsStroke = true; paint.StrokeWidth = IsFocused ? 1.5f : 1;
        canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(rect, -0.75f, -0.75f), Theme.RadiusSmall + 1.25f), paint);

        var x = rect.Left + Padding.Left;
        if (Icon != Icon.None)
        {
            Icons.Draw(canvas, Icon, x + 8, rect.MidY, 16, Theme.TextMuted);
            x += 24;
        }
        var textRight = rect.Right - Padding.Right;
        var style = Style;

        canvas.Save();
        canvas.ClipRect(new SKRect(x, rect.Top, textRight, rect.Bottom));

        if (_text.Length == 0 && !IsFocused)
        {
            TextRender.Draw(canvas, Placeholder, x, rect.MidY, style.With(Theme.TextMuted));
        }
        else
        {
            // Keep the caret in view.
            var caretX = style.Measure(_text.AsSpan(0, _caret));
            var visibleW = textRight - x;
            if (caretX - _scrollX > visibleW - 2) _scrollX = caretX - visibleW + 2;
            if (caretX - _scrollX < 0) _scrollX = caretX;
            _scrollX = Math.Max(0, Math.Min(_scrollX, Math.Max(0, style.Measure(_text) - visibleW + 2)));

            TextRender.Draw(canvas, _text, x - _scrollX, rect.MidY, style);

            if (IsFocused)
            {
                var t = (Root?.Animator is not null ? Environment.TickCount64 / 1000.0 : 0) - _blinkStart;
                var on = (int)(t * 2) % 2 == 0;
                if (on)
                {
                    paint.IsStroke = false; paint.Color = Theme.Accent;
                    canvas.DrawRect(x - _scrollX + caretX, rect.MidY - style.LineHeight / 2 + 2, 1.5f, style.LineHeight - 4, paint);
                }
                Root?.RequestWakeup(0.5 - t % 0.5);   // next blink edge
                Invalidate();
            }
        }
        canvas.Restore();
    }

    protected internal override void OnFocusChanged(bool focused)
    {
        _blinkStart = Environment.TickCount64 / 1000.0;
        base.OnFocusChanged(focused);
    }

    protected internal override void OnPointerDown(PointerEvent e)
    {
        if (e.Button == PointerButton.Left)
        {
            var style = Style;
            var x0 = Bounds.Left + Padding.Left + (Icon != Icon.None ? 24 : 0) - _scrollX;
            var target = e.Position.X - x0;
            var best = 0;
            for (var i = 0; i <= _text.Length; i++)
            {
                if (style.Measure(_text.AsSpan(0, i)) > target) break;
                best = i;
            }
            _caret = best;
            _blinkStart = Environment.TickCount64 / 1000.0;
            e.Handled = true;
        }
        base.OnPointerDown(e);
    }

    protected internal override void OnTextInput(TextInputEvent e)
    {
        Insert(e.Character.ToString());
        e.Handled = true;
    }

    protected internal override void OnKeyDown(KeyEvent e)
    {
        var ctrl = (e.Modifiers & Modifiers.Control) != 0;
        switch (e.Key)
        {
            case Key.Backspace when _caret > 0:
                var from = ctrl ? WordLeft() : _caret - 1;
                _text = _text.Remove(from, _caret - from); _caret = from; Changed(); break;
            case Key.Delete when _caret < _text.Length:
                _text = _text.Remove(_caret, 1); Changed(); break;
            case Key.Left: _caret = ctrl ? WordLeft() : Math.Max(0, _caret - 1); break;
            case Key.Right: _caret = ctrl ? WordRight() : Math.Min(_text.Length, _caret + 1); break;
            case Key.Home: _caret = 0; break;
            case Key.End: _caret = _text.Length; break;
            case Key.Enter: Submitted?.Invoke(_text); break;
            case Key.V when ctrl: Insert(Root?.Clipboard?.GetText() ?? ""); break;
            case Key.A when ctrl: _caret = _text.Length; break;
            case Key.Escape: return;   // let it bubble
            default: base.OnKeyDown(e); return;
        }
        _blinkStart = Environment.TickCount64 / 1000.0;
        e.Handled = true;
        Invalidate();
    }

    private void Insert(string s)
    {
        if (s.Length == 0) return;
        s = s.Replace("\r", "").Replace("\n", "");
        _text = _text.Insert(_caret, s);
        _caret += s.Length;
        Changed();
        Invalidate();
    }

    private void Changed() => TextChanged?.Invoke(_text);

    private int WordLeft()
    {
        var i = _caret;
        while (i > 0 && !char.IsLetterOrDigit(_text[i - 1])) i--;
        while (i > 0 && char.IsLetterOrDigit(_text[i - 1])) i--;
        return i;
    }

    private int WordRight()
    {
        var i = _caret;
        while (i < _text.Length && char.IsLetterOrDigit(_text[i])) i++;
        while (i < _text.Length && !char.IsLetterOrDigit(_text[i])) i++;
        return i;
    }
}

public interface IClipboard
{
    string? GetText();
    void SetText(string text);
}
