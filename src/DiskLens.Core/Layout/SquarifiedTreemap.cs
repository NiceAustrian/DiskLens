namespace DiskLens.Core.Layout;

public readonly record struct LayoutRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float Area => Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool Contains(float px, float py) => px >= X && px < Right && py >= Y && py < Bottom;
    public LayoutRect Deflate(float d) => new(X + d, Y + d, Math.Max(0, Width - 2 * d), Math.Max(0, Height - 2 * d));
}

/// <summary>
/// Squarified treemap layout (Bruls, Huizing, van Wijk 2000). Given weights and a rectangle, produces
/// one sub-rectangle per weight with aspect ratios as close to 1 as the algorithm allows.
/// Pure and allocation-light; the caller walks the tree and recurses.
/// </summary>
public static class SquarifiedTreemap
{
    /// <summary>
    /// Lays out <paramref name="weights"/> into <paramref name="bounds"/>. Weights should be sorted
    /// descending for best results; zero/negative weights get an empty rect. Writes into
    /// <paramref name="output"/> which must be at least <c>weights.Length</c> long.
    /// </summary>
    public static void Layout(ReadOnlySpan<double> weights, LayoutRect bounds, Span<LayoutRect> output)
    {
        if (weights.Length == 0) return;
        if (output.Length < weights.Length) throw new ArgumentException("Output span too small.", nameof(output));

        double total = 0;
        foreach (var w in weights) if (w > 0) total += w;

        if (total <= 0 || bounds.IsEmpty)
        {
            output[..weights.Length].Fill(new LayoutRect(bounds.X, bounds.Y, 0, 0));
            return;
        }

        // Scale so that sum of areas == bounds area.
        var scale = bounds.Area / total;
        var x = (double)bounds.X;
        var y = (double)bounds.Y;
        var w0 = (double)bounds.Width;
        var h0 = (double)bounds.Height;

        var start = 0;
        while (start < weights.Length)
        {
            // Skip non-positive weights.
            if (weights[start] <= 0)
            {
                output[start] = new LayoutRect((float)x, (float)y, 0, 0);
                start++;
                continue;
            }

            var shortSide = Math.Min(w0, h0);
            if (shortSide <= 0)
            {
                for (var i = start; i < weights.Length; i++) output[i] = new LayoutRect((float)x, (float)y, 0, 0);
                return;
            }

            // Grow the row while the worst aspect ratio improves.
            var end = start;           // exclusive
            double rowSum = 0, rowMin = double.MaxValue, rowMax = 0;
            var worst = double.MaxValue;
            while (end < weights.Length && weights[end] > 0)
            {
                var a = weights[end] * scale;
                var newSum = rowSum + a;
                var newMin = Math.Min(rowMin, a);
                var newMax = Math.Max(rowMax, a);
                var newWorst = Worst(newSum, newMin, newMax, shortSide);
                if (newWorst > worst) break;   // adding this item makes the row worse: stop
                rowSum = newSum; rowMin = newMin; rowMax = newMax; worst = newWorst;
                end++;
            }
            if (end == start) end = start + 1;   // guarantee progress

            // Lay the row along the short side.
            var rowThickness = rowSum / shortSide;
            var horizontalRow = w0 >= h0;        // row is a vertical strip on the left when wide, else a horizontal strip on top
            double offset = 0;
            for (var i = start; i < end; i++)
            {
                var a = weights[i] * scale;
                var len = a / rowThickness;
                output[i] = horizontalRow
                    ? new LayoutRect((float)x, (float)(y + offset), (float)rowThickness, (float)len)
                    : new LayoutRect((float)(x + offset), (float)y, (float)len, (float)rowThickness);
                offset += len;
            }

            if (horizontalRow) { x += rowThickness; w0 -= rowThickness; }
            else               { y += rowThickness; h0 -= rowThickness; }
            start = end;
        }
    }

    private static double Worst(double sum, double min, double max, double side)
    {
        var s2 = side * side;
        var sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }
}
