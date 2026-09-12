using SkiaSharp;

namespace DiskLens.UI.Rendering;

/// <summary>
/// A small set of vector icons defined on a 24×24 grid (stroke-based, Lucide-style). Rendered with
/// <see cref="Draw"/> at any size; no bitmaps involved.
/// </summary>
public enum Icon
{
    None, Folder, FolderOpen, File, Drive, Network, Usb, Disc, ArrowLeft, ArrowUp, ChevronRight, ChevronDown,
    Search, Refresh, Close, Warning, Shield, Sun, Moon, Settings, ExternalLink, Copy, Trash, Home, Filter, Stop, Info,
}

public static class Icons
{
    private static readonly Dictionary<Icon, SKPath> Cache = [];

    /// <summary>Draws the icon centred at (cx, cy) with the given pixel size and stroke colour.</summary>
    public static void Draw(SKCanvas canvas, Icon icon, float cx, float cy, float size, SKColor color, float strokeWidth = 1.75f)
    {
        if (icon == Icon.None) return;
        var path = Get(icon);
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = strokeWidth * 24f / size,   // path is scaled by size/24, so this keeps the stroke at strokeWidth px
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.Save();
        canvas.Translate(cx - size / 2, cy - size / 2);
        canvas.Scale(size / 24f);
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    public static SKPath Get(Icon icon)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(icon, out var path))
            {
                path = SKPath.ParseSvgPathData(Svg(icon)) ?? new SKPath();
                Cache[icon] = path;
            }
            return path;
        }
    }

    // Paths are from Lucide (ISC licence), 24×24 viewbox.
    private static string Svg(Icon icon) => icon switch
    {
        Icon.Folder       => "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
        Icon.FolderOpen   => "m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2",
        Icon.File         => "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z M14 2v4a2 2 0 0 0 2 2h4",
        Icon.Drive        => "M22 12H2 M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z M6 16h.01 M10 16h.01",
        Icon.Network      => "M17 16h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z M3 16h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z M10 2h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3 M12 12V8",
        Icon.Usb          => "M10 6a1 1 0 1 0 0 2 1 1 0 1 0 0-2 M4 19a1 1 0 1 0 0 2 1 1 0 1 0 0-2 M4.7 19.3L19 5 M21 3l-3 1 2 2Z M9.26 7.68L5 12l2 5 M10 14l5 2 3.5-3.5 M18 12l1-1 1 1-1 1Z",
        Icon.Disc         => "M12 2a10 10 0 1 0 0 20 10 10 0 1 0 0-20 M12 10a2 2 0 1 0 0 4 2 2 0 1 0 0-4",
        Icon.ArrowLeft    => "m12 19-7-7 7-7 M19 12H5",
        Icon.ArrowUp      => "m5 12 7-7 7 7 M12 19V5",
        Icon.ChevronRight => "m9 18 6-6-6-6",
        Icon.ChevronDown  => "m6 9 6 6 6-6",
        Icon.Search       => "M11 3a8 8 0 1 0 0 16 8 8 0 1 0 0-16 M21 21l-4.3-4.3",
        Icon.Refresh      => "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16 M16 16h5v5",
        Icon.Close        => "M18 6 6 18 M6 6l12 12",
        Icon.Warning      => "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3 M12 9v4 M12 17h.01",
        Icon.Shield       => "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
        Icon.Sun          => "M12 8a4 4 0 1 0 0 8 4 4 0 1 0 0-8 M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41",
        Icon.Moon         => "M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z",
        Icon.Settings     => "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z M12 9a3 3 0 1 0 0 6 3 3 0 1 0 0-6",
        Icon.ExternalLink => "M15 3h6v6 M10 14 21 3 M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
        Icon.Copy         => "M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2z M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",
        Icon.Trash        => "M3 6h18 M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6 M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2",
        Icon.Home         => "m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z M9 22V12h6v10",
        Icon.Filter       => "M22 3H2l8 9.46V19l4 2v-8.54L22 3z",
        Icon.Stop         => "M5 5h14v14H5z",
        Icon.Info         => "M12 2a10 10 0 1 0 0 20 10 10 0 1 0 0-20 M12 16v-4 M12 8h.01",
        _ => "",
    };
}
