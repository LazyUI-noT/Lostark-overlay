namespace GaugeOverlay.Core.Domain;

/// <summary>Text on a coloured fill (capsules, 기능 맵 switches, 감지 chips): black or white, whichever contrasts more
/// (WCAG 1.4.3; audit 2026-11-12).</summary>
public static class Contrast
{
    /// <summary>Black text reads better than white on this fill.</summary>
    public static bool BlackInk(byte r, byte g, byte b)
    {
        double lum = Luminance(r, g, b);
        return (lum + 0.05) / 0.05 >= 1.05 / (lum + 0.05);   // against black : against white
    }

    /// <summary>The light theme's card grey (#F3F3F3), a little darker than white, so a colour that reads on it reads on both.</summary>
    public const byte LightCard = 0xF3;
    const double LineMinimum = 3;   // WCAG 1.4.11: lines and marks

    /// <summary>A line colour made for a dark card, darkened (same hue) until it reaches 3:1 on a light card; one that already does stays.</summary>
    public static (byte R, byte G, byte B) OnLight(byte r, byte g, byte b)
    {
        for (double k = 1; k > 0; k -= 0.02)
        {
            var (dr, dg, db) = ((byte)(r * k), (byte)(g * k), (byte)(b * k));
            if (Ratio(dr, dg, db, LightCard, LightCard, LightCard) >= LineMinimum) return (dr, dg, db);
        }
        return (0, 0, 0);
    }

    /// <summary>WCAG contrast ratio between two colours (1 to 21).</summary>
    public static double Ratio(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        double a = Luminance(r1, g1, b1), c = Luminance(r2, g2, b2);
        return (Math.Max(a, c) + 0.05) / (Math.Min(a, c) + 0.05);
    }

    static double Luminance(byte r, byte g, byte b) => 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);

    static double Linear(byte v)
    {
        double x = v / 255.0;
        return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
    }
}
