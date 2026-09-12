using System.Globalization;

namespace DiskLens.Core;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Human readable size with binary (1024) steps, e.g. "1.5 GB".</summary>
    public static string Format(long bytes, int decimals = 1)
    {
        if (bytes < 0) return "-" + Format(-bytes, decimals);
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var digits = value >= 100 ? 0 : decimals;
        return value.ToString("F" + digits, CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    /// <summary>Splits into (number, unit) for layouts that style them differently.</summary>
    public static (string Number, string Unit) FormatParts(long bytes, int decimals = 1)
    {
        var s = Format(bytes, decimals);
        var space = s.LastIndexOf(' ');
        return (s[..space], s[(space + 1)..]);
    }
}
