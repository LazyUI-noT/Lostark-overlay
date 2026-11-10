namespace GaugeOverlay.Core.Domain;

/// <summary>WCAG contrast between two colours, for text in a colour that also has to mean something (the rail's groups).</summary>
public static class UiContrast
{
    /// <summary>The ratio small text needs.</summary>
    public const double Readable = 4.5;

    const double Step = 0.05;

    public static double Ratio(string first, string second)
    {
        double a = Luminance(Rgb(first)), b = Luminance(Rgb(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary><paramref name="colour"/>, moved towards white on a dark background (black on a light one) only as far as
    /// it takes to read, so it stays its hue.</summary>
    public static string Legible(string colour, string background)
    {
        var (r, g, b) = Rgb(colour);
        double target = Luminance(Rgb(background)) < 0.18 ? 255 : 0;
        for (double mix = 0; mix <= 1; mix += Step)
        {
            string moved = Hex(Blend(r, target, mix), Blend(g, target, mix), Blend(b, target, mix));
            if (Ratio(moved, background) >= Readable) return moved;
        }
        return Hex((int)target, (int)target, (int)target);
    }

    static int Blend(int channel, double target, double mix) => (int)Math.Round(channel + (target - channel) * mix);

    static string Hex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>#RRGGBB or #AARRGGBB; the alpha is ignored.</summary>
    static (int R, int G, int B) Rgb(string hex)
    {
        int value = Convert.ToInt32(hex[^6..], 16);
        return (value >> 16 & 0xFF, value >> 8 & 0xFF, value & 0xFF);
    }

    static double Luminance((int R, int G, int B) c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    static double Linear(int channel)
    {
        double s = channel / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
