using SkiaSharp;

namespace DiskLens.UI.Rendering;

/// <summary>Design tokens. Everything visual reads from here; nothing hard-codes colours.</summary>
public sealed record Theme
{
    public required bool IsDark { get; init; }

    // Surfaces
    public required SKColor Background { get; init; }
    public required SKColor Surface { get; init; }
    public required SKColor SurfaceRaised { get; init; }
    public required SKColor SurfaceHover { get; init; }
    public required SKColor SurfaceActive { get; init; }
    public required SKColor Border { get; init; }
    public required SKColor BorderStrong { get; init; }
    public required SKColor Shadow { get; init; }

    // Text
    public required SKColor Text { get; init; }
    public required SKColor TextSecondary { get; init; }
    public required SKColor TextMuted { get; init; }
    public required SKColor TextOnAccent { get; init; }

    // Semantic
    public required SKColor Accent { get; init; }
    public required SKColor AccentSoft { get; init; }
    public required SKColor AccentAlt { get; init; }
    public required SKColor Success { get; init; }
    public required SKColor Warning { get; init; }
    public required SKColor Danger { get; init; }
    public required SKColor Selection { get; init; }

    // Geometry
    public float RadiusSmall { get; init; } = 6;
    public float Radius { get; init; } = 10;
    public float RadiusLarge { get; init; } = 16;
    public float Spacing { get; init; } = 8;

    // Type scale
    public TextStyle Body => new(Fonts.Regular, 13, Text);
    public TextStyle BodyMedium => new(Fonts.Medium, 13, Text);
    public TextStyle BodySecondary => new(Fonts.Regular, 13, TextSecondary);
    public TextStyle Small => new(Fonts.Regular, 11.5f, TextSecondary);
    public TextStyle SmallMuted => new(Fonts.Regular, 11.5f, TextMuted);
    public TextStyle Caption => new(Fonts.Medium, 11, TextMuted);
    public TextStyle Heading => new(Fonts.SemiBold, 20, Text);
    public TextStyle Title => new(Fonts.SemiBold, 15, Text);
    public TextStyle Display => new(Fonts.Bold, 32, Text);
    public TextStyle Mono => new(Fonts.Mono, 12.5f, Text);
    public TextStyle MonoSecondary => new(Fonts.Mono, 12.5f, TextSecondary);
    public TextStyle MonoSmall => new(Fonts.Mono, 11, TextMuted);

    public static readonly Theme Dark = new()
    {
        IsDark = true,
        Background    = new SKColor(0x0C, 0x0E, 0x12),
        Surface       = new SKColor(0x14, 0x17, 0x1D),
        SurfaceRaised = new SKColor(0x1B, 0x1F, 0x27),
        SurfaceHover  = new SKColor(0xFF, 0xFF, 0xFF, 0x0A),
        SurfaceActive = new SKColor(0xFF, 0xFF, 0xFF, 0x14),
        Border        = new SKColor(0xFF, 0xFF, 0xFF, 0x0F),
        BorderStrong  = new SKColor(0xFF, 0xFF, 0xFF, 0x1E),
        Shadow        = new SKColor(0x00, 0x00, 0x00, 0x80),
        Text          = new SKColor(0xE8, 0xEA, 0xF0),
        TextSecondary = new SKColor(0x9A, 0xA3, 0xB2),
        TextMuted     = new SKColor(0x5E, 0x66, 0x75),
        TextOnAccent  = new SKColor(0x0B, 0x0D, 0x14),
        Accent        = new SKColor(0x7C, 0x9A, 0xFF),
        AccentSoft    = new SKColor(0x7C, 0x9A, 0xFF, 0x26),
        AccentAlt     = new SKColor(0x3F, 0xD6, 0xC6),
        Success       = new SKColor(0x4A, 0xD2, 0x95),
        Warning       = new SKColor(0xFF, 0xB4, 0x54),
        Danger        = new SKColor(0xFF, 0x6B, 0x6B),
        Selection     = new SKColor(0x7C, 0x9A, 0xFF, 0x30),
    };

    public static readonly Theme Light = new()
    {
        IsDark = false,
        Background    = new SKColor(0xF3, 0xF4, 0xF7),
        Surface       = new SKColor(0xFF, 0xFF, 0xFF),
        SurfaceRaised = new SKColor(0xFF, 0xFF, 0xFF),
        SurfaceHover  = new SKColor(0x00, 0x00, 0x00, 0x08),
        SurfaceActive = new SKColor(0x00, 0x00, 0x00, 0x10),
        Border        = new SKColor(0x00, 0x00, 0x00, 0x12),
        BorderStrong  = new SKColor(0x00, 0x00, 0x00, 0x22),
        Shadow        = new SKColor(0x10, 0x14, 0x20, 0x30),
        Text          = new SKColor(0x16, 0x18, 0x1D),
        TextSecondary = new SKColor(0x5B, 0x62, 0x70),
        TextMuted     = new SKColor(0x9A, 0xA1, 0xAD),
        TextOnAccent  = new SKColor(0xFF, 0xFF, 0xFF),
        Accent        = new SKColor(0x4F, 0x6E, 0xF5),
        AccentSoft    = new SKColor(0x4F, 0x6E, 0xF5, 0x22),
        AccentAlt     = new SKColor(0x14, 0xA8, 0x9A),
        Success       = new SKColor(0x22, 0xA8, 0x6E),
        Warning       = new SKColor(0xE0, 0x8A, 0x1E),
        Danger        = new SKColor(0xE0, 0x4A, 0x4A),
        Selection     = new SKColor(0x4F, 0x6E, 0xF5, 0x28),
    };
}
