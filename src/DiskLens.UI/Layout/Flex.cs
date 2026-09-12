using DiskLens.UI.Elements;
using SkiaSharp;

namespace DiskLens.UI.Layout;

public enum Axis { Horizontal, Vertical }
public enum CrossAlign { Start, Center, End, Stretch }
public enum MainAlign { Start, Center, End, SpaceBetween }

/// <summary>
/// One-dimensional flexbox: children are laid out along the main axis; those with <see cref="Element.Flex"/>
/// share the remaining space proportionally.
/// </summary>
public class Flex : Element
{
    public Flex(Axis axis) => Axis = axis;

    public Axis Axis { get; }
    public float Gap { get; set; }
    public Thickness Padding { get; set; }
    public CrossAlign CrossAlign { get; set; } = CrossAlign.Stretch;
    public MainAlign MainAlign { get; set; } = MainAlign.Start;

    private bool Horizontal => Axis == Axis.Horizontal;

    protected override SKSize MeasureContent(SKSize available)
    {
        var inner = new SKSize(Math.Max(0, available.Width - Padding.Horizontal), Math.Max(0, available.Height - Padding.Vertical));
        float main = 0, cross = 0, totalFlex = 0;
        var visible = 0;

        // Pass 1: fixed children at full availability.
        foreach (var c in Children)
        {
            if (!c.IsVisible) continue;
            visible++;
            if (c.Flex > 0) { totalFlex += c.Flex; continue; }
            var s = c.Measure(inner);
            if (Horizontal) { main += s.Width; cross = Math.Max(cross, s.Height); }
            else            { main += s.Height; cross = Math.Max(cross, s.Width); }
        }
        if (visible > 1) main += Gap * (visible - 1);

        // Pass 2: flex children at the share they will actually get, so wrapping text inside them
        // reports the cross size it needs at that width.
        var anyFlex = totalFlex > 0;
        if (anyFlex)
        {
            var mainAvail = Horizontal ? inner.Width : inner.Height;
            var remaining = Math.Max(0, mainAvail - main);
            foreach (var c in Children)
            {
                if (!c.IsVisible || c.Flex <= 0) continue;
                var share = float.IsInfinity(remaining) ? remaining : remaining * c.Flex / totalFlex;
                var s = c.Measure(Horizontal ? new SKSize(share, inner.Height) : new SKSize(inner.Width, share));
                cross = Math.Max(cross, Horizontal ? s.Height : s.Width);
            }
            main = Math.Max(main, mainAvail);
        }
        return Horizontal
            ? new SKSize(main + Padding.Horizontal, cross + Padding.Vertical)
            : new SKSize(cross + Padding.Horizontal, main + Padding.Vertical);
    }

    protected override void ArrangeContent(SKRect bounds)
    {
        var inner = new SKRect(bounds.Left + Padding.Left, bounds.Top + Padding.Top, bounds.Right - Padding.Right, bounds.Bottom - Padding.Bottom);
        var innerSize = new SKSize(Math.Max(0, inner.Width), Math.Max(0, inner.Height));
        var mainAvail = Horizontal ? innerSize.Width : innerSize.Height;

        // Pass 1: measure non-flex children, sum flex weights.
        Span<float> sizes = Children.Count <= 64 ? stackalloc float[Children.Count] : new float[Children.Count];
        float used = 0, totalFlex = 0;
        var visible = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.IsVisible) continue;
            visible++;
            if (c.Flex > 0) { totalFlex += c.Flex; continue; }
            var s = c.Measure(innerSize);
            sizes[i] = Horizontal ? s.Width : s.Height;
            used += sizes[i];
        }
        var gaps = visible > 1 ? Gap * (visible - 1) : 0;
        var remaining = Math.Max(0, mainAvail - used - gaps);

        // Pass 2: distribute leftover to flex children (respecting their min/max on the main axis).
        if (totalFlex > 0)
        {
            for (var i = 0; i < Children.Count; i++)
            {
                var c = Children[i];
                if (!c.IsVisible || c.Flex <= 0) continue;
                var share = remaining * c.Flex / totalFlex;
                var min = Horizontal ? c.MinWidth + c.Margin.Horizontal : c.MinHeight + c.Margin.Vertical;
                var max = Horizontal ? c.MaxWidth + c.Margin.Horizontal : c.MaxHeight + c.Margin.Vertical;
                sizes[i] = Math.Clamp(share, min, max);
            }
            remaining = 0;
        }

        // Main-axis alignment offset.
        float offset = MainAlign switch
        {
            MainAlign.Center => remaining / 2,
            MainAlign.End => remaining,
            _ => 0,
        };
        var spaceBetween = MainAlign == MainAlign.SpaceBetween && visible > 1 ? remaining / (visible - 1) : 0;

        var pos = (Horizontal ? inner.Left : inner.Top) + offset;
        for (var i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.IsVisible) continue;
            var mainSize = sizes[i];

            // Cross-axis size and alignment.
            var crossAvail = Horizontal ? innerSize.Height : innerSize.Width;
            var crossSize = crossAvail;
            var crossOffset = 0f;
            if (CrossAlign != CrossAlign.Stretch)
            {
                var measured = c.Measure(Horizontal ? new SKSize(mainSize, crossAvail) : new SKSize(crossAvail, mainSize));
                crossSize = Math.Min(crossAvail, Horizontal ? measured.Height : measured.Width);
                crossOffset = CrossAlign switch
                {
                    CrossAlign.Center => (crossAvail - crossSize) / 2,
                    CrossAlign.End => crossAvail - crossSize,
                    _ => 0,
                };
            }

            var rect = Horizontal
                ? new SKRect(pos, inner.Top + crossOffset, pos + mainSize, inner.Top + crossOffset + crossSize)
                : new SKRect(inner.Left + crossOffset, pos, inner.Left + crossOffset + crossSize, pos + mainSize);
            c.Arrange(rect);
            pos += mainSize + Gap + spaceBetween;
        }
    }
}

public sealed class Row : Flex
{
    public Row() : base(Axis.Horizontal) { CrossAlign = CrossAlign.Center; }
}

public sealed class Column : Flex
{
    public Column() : base(Axis.Vertical) { }
}

/// <summary>Children overlap, each filling the whole area (or positioned by their margins).</summary>
public sealed class Stack : Element
{
    public Thickness Padding { get; set; }

    protected override SKSize MeasureContent(SKSize available)
    {
        var s = base.MeasureContent(new SKSize(available.Width - Padding.Horizontal, available.Height - Padding.Vertical));
        return new SKSize(s.Width + Padding.Horizontal, s.Height + Padding.Vertical);
    }

    protected override void ArrangeContent(SKRect bounds)
    {
        var inner = new SKRect(bounds.Left + Padding.Left, bounds.Top + Padding.Top, bounds.Right - Padding.Right, bounds.Bottom - Padding.Bottom);
        foreach (var c in Children) c.Arrange(inner);
    }
}

/// <summary>Takes up space. Give it Flex to push siblings apart.</summary>
public sealed class Spacer : Element
{
    public Spacer(float flex = 1) { Flex = flex; IsHitTestVisible = false; }
    protected override SKSize MeasureContent(SKSize available) => SKSize.Empty;
}
