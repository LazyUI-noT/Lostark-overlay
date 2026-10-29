using System.Globalization;

namespace GaugeOverlay.Core.Domain;

/// <summary>An opaque colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>"#RRGGBB" colour codes as the settings store them, and text contrast (WCAG 2.x) for the themes.</summary>
public static class HexColor
{
    /// <summary>Reads "#RGB", "#RRGGBB" or "#AARRGGBB" (alpha ignored), any case, surrounding spaces allowed.</summary>
    public static bool TryParse(string? text, out Rgb rgb)
    {
        rgb = default;
        var t = text?.Trim();
        if (t is not { Length: 4 or 7 or 9 } || t[0] != '#' || !uint.TryParse(t.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint v))
            return false;
        rgb = t.Length == 4
            ? new((byte)((v >> 8 & 0xF) * 17), (byte)((v >> 4 & 0xF) * 17), (byte)((v & 0xF) * 17))
            : new((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>"#RRGGBB", upper case.</summary>
    public static string Format(Rgb c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>The contrast ratio between two colours, 1 (same) to 21 (black on white), either order.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    static double Luminance(Rgb c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    static double Linear(byte v)
    {
        double x = v / 255.0;
        return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
    }
}
