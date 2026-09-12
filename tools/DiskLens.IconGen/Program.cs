// Renders the DiskLens app icon at several sizes and packs them into a Windows .ico (PNG-compressed
// entries) plus a large PNG for docs. Run: dotnet run --project tools/DiskLens.IconGen -- <outDir>
using SkiaSharp;

var outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
Directory.CreateDirectory(outDir);

int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var pngs = new List<(int Size, byte[] Data)>();
foreach (var size in sizes)
{
    using var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bmp)) DiskLensIcon.Draw(canvas, size);
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    pngs.Add((size, data.ToArray()));
}

File.WriteAllBytes(Path.Combine(outDir, "icon-256.png"), pngs.Single(p => p.Size == 256).Data);
using (var bmp = new SKBitmap(1024, 1024))
{
    using (var canvas = new SKCanvas(bmp)) DiskLensIcon.Draw(canvas, 1024);
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(outDir, "icon-1024.png"), data.ToArray());
}

// ICO: header + directory entries + PNG blobs.
using (var ico = new BinaryWriter(File.Create(Path.Combine(outDir, "app.ico"))))
{
    ico.Write((ushort)0); ico.Write((ushort)1); ico.Write((ushort)pngs.Count);
    var offset = 6 + 16 * pngs.Count;
    foreach (var (size, data) in pngs)
    {
        ico.Write((byte)(size >= 256 ? 0 : size));
        ico.Write((byte)(size >= 256 ? 0 : size));
        ico.Write((byte)0); ico.Write((byte)0);
        ico.Write((ushort)1); ico.Write((ushort)32);
        ico.Write(data.Length); ico.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in pngs) ico.Write(data);
}
Console.WriteLine($"Wrote icons to {outDir}");

static class DiskLensIcon
{
    /// <summary>A dark rounded tile with a lens ring; inside, a small treemap in the app's accent colours.</summary>
    public static void Draw(SKCanvas canvas, float s)
    {
        canvas.Clear(SKColors.Transparent);
        var r = new SKRect(0, 0, s, s);
        var radius = s * 0.22f;

        using var paint = new SKPaint { IsAntialias = true };

        // Tile
        paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(s, s),
            [new SKColor(0x23, 0x28, 0x33), new SKColor(0x0F, 0x12, 0x18)], SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(new SKRoundRect(r, radius), paint);
        paint.Shader = null;

        // Subtle top highlight border
        paint.IsStroke = true; paint.StrokeWidth = Math.Max(1, s * 0.012f);
        paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, s),
            [new SKColor(255, 255, 255, 0x40), new SKColor(255, 255, 255, 0x08)], SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(r, -paint.StrokeWidth / 2, -paint.StrokeWidth / 2), radius), paint);
        paint.Shader = null; paint.IsStroke = false;

        // Treemap inside the lens
        var c = new SKPoint(s * 0.5f, s * 0.5f);
        var lensR = s * 0.31f;
        canvas.Save();
        using (var clip = new SKPath()) { clip.AddCircle(c.X, c.Y, lensR); canvas.ClipPath(clip, antialias: true); }
        var box = new SKRect(c.X - lensR, c.Y - lensR, c.X + lensR, c.Y + lensR);
        var gap = Math.Max(1, s * 0.018f);
        var split = box.Left + box.Width * 0.58f;
        var splitY = box.Top + box.Height * 0.55f;
        Fill(canvas, paint, new SKRect(box.Left, box.Top, split - gap / 2, box.Bottom), new SKColor(0x7C, 0x9A, 0xFF));
        Fill(canvas, paint, new SKRect(split + gap / 2, box.Top, box.Right, splitY - gap / 2), new SKColor(0x3F, 0xD6, 0xC6));
        Fill(canvas, paint, new SKRect(split + gap / 2, splitY + gap / 2, box.Right, box.Bottom), new SKColor(0xE8, 0x6A, 0xB0));
        canvas.Restore();

        // Lens ring
        paint.IsStroke = true; paint.StrokeWidth = s * 0.07f; paint.Color = new SKColor(0xF2, 0xF4, 0xF8);
        canvas.DrawCircle(c, lensR + paint.StrokeWidth * 0.35f, paint);

        // Handle (bottom-right), rounded
        paint.StrokeCap = SKStrokeCap.Round; paint.StrokeWidth = s * 0.085f;
        var dir = new SKPoint(0.7071f, 0.7071f);
        var start = new SKPoint(c.X + dir.X * (lensR + s * 0.06f), c.Y + dir.Y * (lensR + s * 0.06f));
        var end = new SKPoint(c.X + dir.X * (lensR + s * 0.20f), c.Y + dir.Y * (lensR + s * 0.20f));
        canvas.DrawLine(start, end, paint);
    }

    private static void Fill(SKCanvas canvas, SKPaint paint, SKRect r, SKColor color)
    {
        paint.IsStroke = false;
        paint.Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom),
            [Lighten(color, 0.18f), color, Darken(color, 0.25f)], [0f, 0.5f, 1f], SKShaderTileMode.Clamp);
        canvas.DrawRect(r, paint);
        paint.Shader = null;
    }

    private static SKColor Lighten(SKColor c, float t) => new((byte)(c.Red + (255 - c.Red) * t), (byte)(c.Green + (255 - c.Green) * t), (byte)(c.Blue + (255 - c.Blue) * t));
    private static SKColor Darken(SKColor c, float t) => new((byte)(c.Red * (1 - t)), (byte)(c.Green * (1 - t)), (byte)(c.Blue * (1 - t)));
}
