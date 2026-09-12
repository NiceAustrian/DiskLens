using SkiaSharp;

namespace DiskLens.UI.Rendering;

public enum TextAlign { Left, Center, Right }

/// <summary>Immutable description of how to draw text. Fonts are cached per (typeface, size).</summary>
public sealed record TextStyle(SKTypeface Typeface, float Size, SKColor Color)
{
    private static readonly Dictionary<(SKTypeface, float), SKFont> FontCache = [];

    public TextStyle With(SKColor color) => this with { Color = color };
    public TextStyle With(float size) => this with { Size = size };
    public TextStyle With(SKTypeface typeface) => this with { Typeface = typeface };

    public SKFont Font
    {
        get
        {
            lock (FontCache)
            {
                if (!FontCache.TryGetValue((Typeface, Size), out var font))
                {
                    font = new SKFont(Typeface, Size)
                    {
                        Edging = SKFontEdging.SubpixelAntialias,
                        Subpixel = true,
                        Hinting = SKFontHinting.Slight,
                    };
                    FontCache[(Typeface, Size)] = font;
                }
                return font;
            }
        }
    }

    /// <summary>Distance from baseline to the top of the tallest glyph (positive).</summary>
    public float Ascent => -Font.Metrics.Ascent;
    public float Descent => Font.Metrics.Descent;
    public float LineHeight => Font.Spacing;

    public float Measure(string text) => Font.MeasureText(text);
    public float Measure(ReadOnlySpan<char> text) => Font.MeasureText(text);
}

public static class TextRender
{
    private const string Ellipsis = "\u2026";

    /// <summary>Draws single-line text with its vertical centre at <paramref name="centerY"/>.</summary>
    public static void Draw(SKCanvas canvas, string text, float x, float centerY, TextStyle style, TextAlign align = TextAlign.Left, SKPaint? paint = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = style.Font;
        var baseline = centerY + (style.Ascent - style.Descent) / 2;
        using var own = paint is null ? new SKPaint { Color = style.Color, IsAntialias = true } : null;
        canvas.DrawText(text, x, baseline, ToSk(align), font, paint ?? own!);
    }

    /// <summary>Draws text at the baseline.</summary>
    public static void DrawBaseline(SKCanvas canvas, string text, float x, float baseline, TextStyle style, TextAlign align = TextAlign.Left)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var paint = new SKPaint { Color = style.Color, IsAntialias = true };
        canvas.DrawText(text, x, baseline, ToSk(align), style.Font, paint);
    }

    /// <summary>Draws text truncated with an ellipsis if it does not fit into <paramref name="maxWidth"/>.</summary>
    public static void DrawEllipsized(SKCanvas canvas, string text, float x, float centerY, float maxWidth, TextStyle style, TextAlign align = TextAlign.Left)
    {
        if (maxWidth <= 0 || string.IsNullOrEmpty(text)) return;
        var fitted = Ellipsize(text, style, maxWidth);
        if (fitted.Length == 0) return;
        Draw(canvas, fitted, x, centerY, style, align);
    }

    /// <summary>Middle-truncates paths ("C:\Users\…\file.txt") so both ends stay readable.</summary>
    public static void DrawEllipsizedMiddle(SKCanvas canvas, string text, float x, float centerY, float maxWidth, TextStyle style, TextAlign align = TextAlign.Left)
    {
        if (maxWidth <= 0 || string.IsNullOrEmpty(text)) return;
        Draw(canvas, EllipsizeMiddle(text, style, maxWidth), x, centerY, style, align);
    }

    public static string Ellipsize(string text, TextStyle style, float maxWidth)
    {
        if (style.Measure(text) <= maxWidth) return text;
        var ellipsisWidth = style.Measure(Ellipsis);
        var budget = maxWidth - ellipsisWidth;
        if (budget <= 0) return "";

        // Binary search the longest prefix that fits.
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (style.Measure(text.AsSpan(0, mid)) <= budget) lo = mid; else hi = mid - 1;
        }
        return lo == 0 ? "" : text[..lo].TrimEnd() + Ellipsis;
    }

    public static string EllipsizeMiddle(string text, TextStyle style, float maxWidth)
    {
        if (style.Measure(text) <= maxWidth) return text;
        var budget = maxWidth - style.Measure(Ellipsis);
        if (budget <= 0) return "";

        var keep = text.Length;
        while (keep > 0)
        {
            var head = (keep + 1) / 2;
            var tail = keep - head;
            var candidate = string.Concat(text.AsSpan(0, head), Ellipsis, text.AsSpan(text.Length - tail));
            if (style.Measure(candidate) <= maxWidth) return candidate;
            keep--;
        }
        return Ellipsis;
    }

    private static SKTextAlign ToSk(TextAlign align) => align switch
    {
        TextAlign.Center => SKTextAlign.Center,
        TextAlign.Right => SKTextAlign.Right,
        _ => SKTextAlign.Left,
    };
}
