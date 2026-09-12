using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

public enum ButtonStyle { Primary, Secondary, Ghost, Danger }

/// <summary>A button with optional icon, hover/press animation and keyboard activation.</summary>
public class Button : Element
{
    private readonly Tween _hover = new(0);
    private readonly Tween _press = new(0);

    public Button(string text = "", Icon icon = Icon.None)
    {
        Text = text;
        Icon = icon;
        Cursor = CursorKind.Hand;
        CanFocus = true;
    }

    public string Text { get; set; }
    public Icon Icon { get; set; }
    public ButtonStyle Style { get; set; } = ButtonStyle.Secondary;
    public float IconSize { get; set; } = 16;
    public Thickness Padding { get; set; } = new(14, 8);
    public bool IsEnabled { get; set; } = true;
    public bool IsCompact { get; set; }

    public event Action? Activated;

    protected override SKSize MeasureContent(SKSize available)
    {
        var style = TextStyle;
        var w = Padding.Horizontal;
        if (Icon != Icon.None) w += IconSize + (Text.Length > 0 ? 8 : 0);
        if (Text.Length > 0) w += style.Measure(Text);
        var h = Math.Max(style.LineHeight, IconSize) + Padding.Vertical;
        return new SKSize(w, h);
    }

    private TextStyle TextStyle => Theme.BodyMedium.With(ForegroundColor);

    private SKColor ForegroundColor => Style switch
    {
        ButtonStyle.Primary => Theme.TextOnAccent,
        ButtonStyle.Danger => Theme.Danger,
        ButtonStyle.Ghost => Theme.TextSecondary,
        _ => Theme.Text,
    };

    protected override void OnDraw(SKCanvas canvas)
    {
        var rect = Bounds;
        var rr = new SKRoundRect(rect, Theme.RadiusSmall + 2);
        var hover = _hover.Value;
        var press = _press.Value;

        using var paint = new SKPaint { IsAntialias = true };
        switch (Style)
        {
            case ButtonStyle.Primary:
                paint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                    [Lighten(Theme.Accent, 0.08f * hover), Theme.Accent], SKShaderTileMode.Clamp);
                canvas.DrawRoundRect(rr, paint);
                paint.Shader = null;
                break;
            case ButtonStyle.Secondary:
                paint.Color = Theme.SurfaceRaised;
                canvas.DrawRoundRect(rr, paint);
                paint.Color = Theme.BorderStrong;
                paint.IsStroke = true; paint.StrokeWidth = 1;
                canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(rect, -0.5f, -0.5f), Theme.RadiusSmall + 1.5f), paint);
                paint.IsStroke = false;
                break;
            case ButtonStyle.Danger:
                paint.Color = Theme.Danger.WithAlpha(0x22);
                canvas.DrawRoundRect(rr, paint);
                break;
        }

        // Hover / press overlays
        if (hover > 0)
        {
            paint.Color = Theme.SurfaceHover.WithAlpha((byte)(Theme.SurfaceHover.Alpha * hover));
            canvas.DrawRoundRect(rr, paint);
        }
        if (press > 0)
        {
            paint.Color = Theme.SurfaceActive.WithAlpha((byte)(Theme.SurfaceActive.Alpha * press));
            canvas.DrawRoundRect(rr, paint);
        }
        if (IsFocused && Root?.Focused == this)
        {
            paint.Color = Theme.Accent.WithAlpha(0x80);
            paint.IsStroke = true; paint.StrokeWidth = 1.5f;
            canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(rect, 1.5f, 1.5f), Theme.RadiusSmall + 3.5f), paint);
            paint.IsStroke = false;
        }

        // Content
        var style = TextStyle;
        var fg = IsEnabled ? ForegroundColor : ForegroundColor.WithAlpha(0x66);
        var contentW = (Icon != Icon.None ? IconSize + (Text.Length > 0 ? 8 : 0) : 0) + (Text.Length > 0 ? style.Measure(Text) : 0);
        var x = rect.MidX - contentW / 2;
        if (Icon != Icon.None)
        {
            Icons.Draw(canvas, Icon, x + IconSize / 2, rect.MidY, IconSize, fg);
            x += IconSize + 8;
        }
        if (Text.Length > 0) TextRender.Draw(canvas, Text, x, rect.MidY, style.With(fg));
    }

    protected internal override void OnPointerEnter()
    {
        base.OnPointerEnter();
        Animate(_hover.To(1, 0.12f));
    }

    protected internal override void OnPointerExit()
    {
        base.OnPointerExit();
        Animate(_hover.To(0, 0.2f));
    }

    protected internal override void OnPointerDown(PointerEvent e)
    {
        if (e.Button == PointerButton.Left) { Animate(_press.To(1, 0.05f)); e.Handled = true; }
        base.OnPointerDown(e);
    }

    protected internal override void OnPointerUp(PointerEvent e)
    {
        Animate(_press.To(0, 0.2f));
        base.OnPointerUp(e);
    }

    protected internal override void OnClick(PointerEvent e)
    {
        if (!IsEnabled) return;
        if (e.Button == PointerButton.Left)
        {
            e.Handled = true;
            Activated?.Invoke();
        }
        base.OnClick(e);
    }

    protected internal override void OnKeyDown(KeyEvent e)
    {
        if (IsEnabled && e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            Activated?.Invoke();
        }
        base.OnKeyDown(e);
    }

    private static SKColor Lighten(SKColor c, float amount) =>
        new((byte)Math.Min(255, c.Red + 255 * amount), (byte)Math.Min(255, c.Green + 255 * amount), (byte)Math.Min(255, c.Blue + 255 * amount), c.Alpha);
}

/// <summary>Square icon-only button; ghost by default.</summary>
public sealed class IconButton : Button
{
    public IconButton(Icon icon, string? tooltip = null) : base("", icon)
    {
        Style = ButtonStyle.Ghost;
        Padding = new Thickness(8);
        Tooltip = tooltip;
        IconSize = 18;
    }
}
