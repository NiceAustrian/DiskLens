using DiskLens.UI.Elements;
using DiskLens.UI.Rendering;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

public enum Ellipsis { None, End, Middle }

/// <summary>Single-line text. Style defaults to the theme's body style; override via <see cref="Style"/> or <see cref="StyleSelector"/>.</summary>
public class Label : Element
{
    private string _text;

    public Label(string text = "")
    {
        _text = text;
        IsHitTestVisible = false;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            InvalidateLayout();
        }
    }

    /// <summary>Fixed style, or null to use <see cref="StyleSelector"/>.</summary>
    public TextStyle? Style { get; set; }

    /// <summary>Resolves the style from the current theme each draw; lets labels follow theme changes.</summary>
    public Func<Theme, TextStyle> StyleSelector { get; set; } = t => t.Body;

    public TextAlign Align { get; set; } = TextAlign.Left;
    public Ellipsis Ellipsis { get; set; } = Ellipsis.End;

    protected TextStyle ResolveStyle() => Style ?? StyleSelector(Theme);

    protected override SKSize MeasureContent(SKSize available)
    {
        var style = ResolveStyle();
        return new SKSize(Math.Min(available.Width, style.Measure(_text)), style.LineHeight);
    }

    protected override void OnDraw(SKCanvas canvas)
    {
        var style = ResolveStyle();
        var x = Align switch
        {
            TextAlign.Center => Bounds.MidX,
            TextAlign.Right => Bounds.Right,
            _ => Bounds.Left,
        };
        switch (Ellipsis)
        {
            case Ellipsis.End: TextRender.DrawEllipsized(canvas, _text, x, Bounds.MidY, Bounds.Width, style, Align); break;
            case Ellipsis.Middle: TextRender.DrawEllipsizedMiddle(canvas, _text, x, Bounds.MidY, Bounds.Width, style, Align); break;
            default: TextRender.Draw(canvas, _text, x, Bounds.MidY, style, Align); break;
        }
    }
}
