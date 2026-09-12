namespace DiskLens.Presentation;

/// <summary>Embedded assets shared by every host (icon for title bars, splash, ...).</summary>
public static class PresentationAssets
{
    private static byte[]? _icon;

    public static byte[] IconPng
    {
        get
        {
            if (_icon is not null) return _icon;
            using var s = typeof(PresentationAssets).Assembly.GetManifestResourceStream("icon-256.png")!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return _icon = ms.ToArray();
        }
    }
}
