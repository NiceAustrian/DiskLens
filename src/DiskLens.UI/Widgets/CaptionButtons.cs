using DiskLens.UI.Elements;
using DiskLens.UI.Hosting;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

/// <summary>
/// Minimise / maximise / close buttons for a custom title bar, drawn in the Windows 11 idiom.
/// Hover and press state come from the OS through <see cref="IWindowChrome"/>; input never reaches
/// this element directly because the OS treats the area as non-client.
/// </summary>
public sealed class CaptionButtons : Element
{
    private readonly IWindowChrome _chrome;
    public const float ButtonWidth = 46;

    public CaptionButtons(IWindowChrome chrome)
    {
        _chrome = chrome;
        IsHitTestVisible = true;
        FixedWidth = ButtonWidth * 3;
        chrome.Changed += Invalidate;
    }

    /// <summary>Which button lies at a logical point, for the OS hit test.</summary>
    public CaptionHit HitAt(SKPoint p)
    {
        if (!Bounds.Contains(p)) return CaptionHit.None;
        var i = (int)((p.X - Bounds.Left) / ButtonWidth);
        return i switch { 0 => CaptionHit.Minimize, 1 => CaptionHit.Maximize, _ => CaptionHit.Close };
    }

    protected override SKSize MeasureContent(SKSize available) => new(ButtonWidth * 3, available.Height);

    protected override void OnDraw(SKCanvas canvas)
    {
        var t = Theme;
        using var paint = new SKPaint { IsAntialias = true };
        using var stroke = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 1 };
        var hovered = _chrome.HoveredButton;
        var pressed = _chrome.PressedButton;

        for (var i = 0; i < 3; i++)
        {
            var hit = i switch { 0 => CaptionHit.Minimize, 1 => CaptionHit.Maximize, _ => CaptionHit.Close };
            var r = new SKRect(Bounds.Left + i * ButtonWidth, Bounds.Top, Bounds.Left + (i + 1) * ButtonWidth, Bounds.Bottom);
            var isClose = hit == CaptionHit.Close;
            var isHover = hovered == hit;
            var isPressed = pressed == hit && isHover;

            if (isHover || isPressed)
            {
                paint.Color = isClose
                    ? new SKColor(0xE8, 0x11, 0x23, (byte)(isPressed ? 0xC0 : 0xFF))
                    : (isPressed ? t.SurfaceActive : t.SurfaceHover);
                canvas.DrawRect(r, paint);
            }

            var fg = isClose && (isHover || isPressed) ? SKColors.White : t.Text.WithAlpha(0xD8);
            stroke.Color = fg;
            var cx = r.MidX; var cy = r.MidY;
            switch (hit)
            {
                case CaptionHit.Minimize:
                    canvas.DrawLine(cx - 5, cy + 0.5f, cx + 5, cy + 0.5f, stroke);
                    break;
                case CaptionHit.Maximize when _chrome.IsMaximized:
                    // restore: two overlapping squares
                    canvas.DrawRect(new SKRect(cx - 5, cy - 3, cx + 3, cy + 5), stroke);
                    canvas.DrawLine(cx - 3, cy - 3, cx - 3, cy - 5, stroke);
                    canvas.DrawLine(cx - 3, cy - 5, cx + 5, cy - 5, stroke);
                    canvas.DrawLine(cx + 5, cy - 5, cx + 5, cy + 3, stroke);
                    canvas.DrawLine(cx + 5, cy + 3, cx + 3, cy + 3, stroke);
                    break;
                case CaptionHit.Maximize:
                    canvas.DrawRoundRect(new SKRoundRect(new SKRect(cx - 5, cy - 5, cx + 5, cy + 5), 1.5f), stroke);
                    break;
                default:
                    canvas.DrawLine(cx - 5, cy - 5, cx + 5, cy + 5, stroke);
                    canvas.DrawLine(cx - 5, cy + 5, cx + 5, cy - 5, stroke);
                    break;
            }
        }
    }
}
