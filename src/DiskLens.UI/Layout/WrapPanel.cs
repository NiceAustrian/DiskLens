using DiskLens.UI.Elements;
using SkiaSharp;

namespace DiskLens.UI.Layout;

/// <summary>Lays children out left-to-right in rows of equal-width cells; wraps when the row is full.</summary>
public sealed class WrapPanel : Element
{
    public float CellWidth { get; set; } = 220;
    public float CellHeight { get; set; } = 140;
    public float Gap { get; set; } = 12;
    public Thickness Padding { get; set; }
    /// <summary>Stretch cells to fill the row width instead of leaving a ragged right edge.</summary>
    public bool Justify { get; set; } = true;

    private int ColumnsFor(float width)
    {
        var inner = width - Padding.Horizontal;
        return Math.Max(1, (int)MathF.Floor((inner + Gap) / (CellWidth + Gap)));
    }

    protected override SKSize MeasureContent(SKSize available)
    {
        var visible = Children.Count(c => c.IsVisible);
        if (visible == 0) return new SKSize(available.Width, Padding.Vertical);
        var cols = ColumnsFor(available.Width);
        var rows = (visible + cols - 1) / cols;
        return new SKSize(available.Width, Padding.Vertical + rows * CellHeight + (rows - 1) * Gap);
    }

    protected override void ArrangeContent(SKRect bounds)
    {
        var cols = ColumnsFor(bounds.Width);
        var inner = bounds.Width - Padding.Horizontal;
        var cellW = Justify ? (inner - Gap * (cols - 1)) / cols : CellWidth;
        var i = 0;
        foreach (var c in Children)
        {
            if (!c.IsVisible) continue;
            var col = i % cols;
            var row = i / cols;
            var x = bounds.Left + Padding.Left + col * (cellW + Gap);
            var y = bounds.Top + Padding.Top + row * (CellHeight + Gap);
            c.Arrange(new SKRect(x, y, x + cellW, y + CellHeight));
            i++;
        }
    }
}
