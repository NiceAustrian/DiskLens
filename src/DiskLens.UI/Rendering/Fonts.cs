using System.Reflection;
using SkiaSharp;

namespace DiskLens.UI.Rendering;

public enum FontWeight { Regular, Medium, SemiBold, Bold }

/// <summary>Embedded typefaces. Inter for UI text, JetBrains Mono for numbers and paths.</summary>
public static class Fonts
{
    private static readonly Lazy<SKTypeface> RegularLazy    = new(() => Load("Inter-Regular.ttf"));
    private static readonly Lazy<SKTypeface> MediumLazy     = new(() => Load("Inter-Medium.ttf"));
    private static readonly Lazy<SKTypeface> SemiBoldLazy   = new(() => Load("Inter-SemiBold.ttf"));
    private static readonly Lazy<SKTypeface> BoldLazy       = new(() => Load("Inter-Bold.ttf"));
    private static readonly Lazy<SKTypeface> MonoLazy       = new(() => Load("JetBrainsMono-Regular.ttf"));
    private static readonly Lazy<SKTypeface> MonoMediumLazy = new(() => Load("JetBrainsMono-Medium.ttf"));

    public static SKTypeface Regular    => RegularLazy.Value;
    public static SKTypeface Medium     => MediumLazy.Value;
    public static SKTypeface SemiBold   => SemiBoldLazy.Value;
    public static SKTypeface Bold       => BoldLazy.Value;
    public static SKTypeface Mono       => MonoLazy.Value;
    public static SKTypeface MonoMedium => MonoMediumLazy.Value;

    public static SKTypeface Get(FontWeight weight) => weight switch
    {
        FontWeight.Medium => Medium,
        FontWeight.SemiBold => SemiBold,
        FontWeight.Bold => Bold,
        _ => Regular,
    };

    private static SKTypeface Load(string file)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(file, StringComparison.Ordinal))
                   ?? throw new FileNotFoundException($"Embedded font '{file}' not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        return SKTypeface.FromStream(stream) ?? throw new InvalidDataException($"Could not load font '{file}'.");
    }
}
