using DiskLens.Core.Layout;

namespace DiskLens.Tests;

public class SquarifiedTreemapTests
{
    [Fact]
    public void Areas_are_proportional_to_weights_and_fill_the_bounds()
    {
        double[] weights = [6, 6, 4, 3, 2, 2, 1];
        var bounds = new LayoutRect(0, 0, 600, 400);
        var rects = new LayoutRect[weights.Length];

        SquarifiedTreemap.Layout(weights, bounds, rects);

        var total = weights.Sum();
        for (var i = 0; i < weights.Length; i++)
        {
            var expected = bounds.Area * weights[i] / total;
            Assert.InRange(rects[i].Area, expected - 0.5, expected + 0.5);
            Assert.True(rects[i].X >= bounds.X - 1e-3 && rects[i].Right <= bounds.Right + 1e-3, $"rect {i} out of bounds horizontally");
            Assert.True(rects[i].Y >= bounds.Y - 1e-3 && rects[i].Bottom <= bounds.Bottom + 1e-3, $"rect {i} out of bounds vertically");
        }
        Assert.InRange(rects.Sum(r => r.Area), bounds.Area - 1, bounds.Area + 1);
    }

    [Fact]
    public void Rects_do_not_overlap()
    {
        double[] weights = [10, 8, 7, 5, 4, 3, 2, 1, 1, 1];
        var rects = new LayoutRect[weights.Length];
        SquarifiedTreemap.Layout(weights, new LayoutRect(10, 20, 300, 200), rects);

        for (var i = 0; i < rects.Length; i++)
        for (var j = i + 1; j < rects.Length; j++)
        {
            var a = rects[i];
            var b = rects[j];
            var overlapW = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
            var overlapH = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            Assert.False(overlapW > 1e-3 && overlapH > 1e-3, $"rects {i} and {j} overlap");
        }
    }

    [Fact]
    public void Squarified_beats_naive_slicing_on_aspect_ratio()
    {
        double[] weights = [6, 6, 4, 3, 2, 2, 1];
        var rects = new LayoutRect[weights.Length];
        SquarifiedTreemap.Layout(weights, new LayoutRect(0, 0, 600, 400), rects);

        var worst = rects.Max(r => Math.Max(r.Width / r.Height, r.Height / r.Width));
        // The canonical example from the paper yields a worst aspect ratio of 2.5; plain slicing would be > 10.
        Assert.True(worst < 3, $"worst aspect ratio {worst}");
    }

    [Fact]
    public void Zero_weights_get_empty_rects()
    {
        double[] weights = [5, 0, 3, 0];
        var rects = new LayoutRect[weights.Length];
        SquarifiedTreemap.Layout(weights, new LayoutRect(0, 0, 100, 100), rects);

        Assert.True(rects[1].IsEmpty);
        Assert.True(rects[3].IsEmpty);
        Assert.InRange(rects[0].Area + rects[2].Area, 9999, 10001);
    }

    [Fact]
    public void Single_item_fills_bounds()
    {
        var rects = new LayoutRect[1];
        SquarifiedTreemap.Layout([42], new LayoutRect(5, 5, 50, 30), rects);
        Assert.Equal(new LayoutRect(5, 5, 50, 30), rects[0]);
    }
}
