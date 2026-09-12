using DiskLens.UI.Elements;
using SkiaSharp;

namespace DiskLens.UI.Layout;

/// <summary>
/// A decorated container: background, border, corner radius, drop shadow, padding. Children fill the
/// padded interior (use a Column/Row inside for multiple children).
/// </summary>
public class Box : Element
{
    public Thickness Padding { get; set; }
    public SKColor? Background { get; set; }
    public SKColor? BorderColor { get; set; }
    public float BorderWidth { get; set; } = 1;
    public float CornerRadius { get; set; }
    public float ShadowBlur { get; set; }
    public float ShadowOffsetY { get; set; } = 2;
    public SKColor? ShadowColor { get; set; }
    /// <summary>Optional vertical gradient drawn over the background.</summary>
    public (SKColor Top, SKColor Bottom)? Gradient { get; set; }

    protected override SKSize MeasureContent(SKSize available)
    {
        var s = base.MeasureContent(new SKSize(Math.Max(0, available.Width - Padding.Horizontal), Math.Max(0, available.Height - Padding.Vertical)));
        return new SKSize(s.Width + Padding.Horizontal, s.Height + Padding.Vertical);
    }

    protected override void ArrangeContent(SKRect bounds)
    {
        var inner = new SKRect(bounds.Left + Padding.Left, bounds.Top + Padding.Top, bounds.Right - Padding.Right, bounds.Bottom - Padding.Bottom);
        foreach (var c in Children) c.Arrange(inner);
    }

    protected override void OnDraw(SKCanvas canvas)
    {
        var rect = Bounds;
        if (rect.IsEmpty) return;
        var rr = new SKRoundRect(rect, CornerRadius);

        if (ShadowBlur > 0)
        {
            using var shadow = new SKPaint
            {
                Color = ShadowColor ?? Theme.Shadow,
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, ShadowBlur),
            };
            var shadowRect = new SKRoundRect(SKRect.Create(rect.Left, rect.Top + ShadowOffsetY, rect.Width, rect.Height), CornerRadius);
            canvas.DrawRoundRect(shadowRect, shadow);
        }

        if (Background is { } bg)
        {
            using var fill = new SKPaint { Color = bg, IsAntialias = true };
            canvas.DrawRoundRect(rr, fill);
        }

        if (Gradient is { } g)
        {
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Left, rect.Bottom), [g.Top, g.Bottom], SKShaderTileMode.Clamp),
            };
            canvas.DrawRoundRect(rr, fill);
        }

        if (BorderColor is { } bc && BorderWidth > 0)
        {
            using var stroke = new SKPaint { Color = bc, IsAntialias = true, IsStroke = true, StrokeWidth = BorderWidth };
            var inset = BorderWidth / 2;
            canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(rect, -inset, -inset), Math.Max(0, CornerRadius - inset)), stroke);
        }
    }
}

/// <summary>A themed card: raised surface with border and a soft shadow.</summary>
public sealed class Card : Box
{
    public Card()
    {
        Padding = new Thickness(16);
    }

    protected override void OnDraw(SKCanvas canvas)
    {
        Background ??= Theme.Surface;
        BorderColor ??= Theme.Border;
        if (CornerRadius == 0) CornerRadius = Theme.RadiusLarge;
        base.OnDraw(canvas);
    }
}

/// <summary>A thin separator line.</summary>
public sealed class Divider : Element
{
    public Axis Axis { get; set; } = Axis.Horizontal;
    public float Inset { get; set; }

    public Divider()
    {
        IsHitTestVisible = false;
    }

    protected override SKSize MeasureContent(SKSize available) =>
        Axis == Axis.Horizontal ? new SKSize(available.Width, 1) : new SKSize(1, available.Height);

    protected override void OnDraw(SKCanvas canvas)
    {
        using var paint = new SKPaint { Color = Theme.Border };
        if (Axis == Axis.Horizontal)
            canvas.DrawRect(Bounds.Left + Inset, Bounds.MidY - 0.5f, Bounds.Width - 2 * Inset, 1, paint);
        else
            canvas.DrawRect(Bounds.MidX - 0.5f, Bounds.Top + Inset, 1, Bounds.Height - 2 * Inset, paint);
    }
}
