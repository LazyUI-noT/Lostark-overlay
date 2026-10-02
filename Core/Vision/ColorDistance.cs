namespace GaugeOverlay.Core.Vision;

/// <summary>Whether two "#RRGGBB" colours are hard to tell apart (CIE76 ΔE), for the gold-frame warning in the settings.</summary>
public static class ColorDistance
{
    /// <summary>ΔE (CIE76) under which a box colour is hard to tell from the gold frame drawn on it.</summary>
    public const double CloseDistance = 20;

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are hard to tell apart (the settings warn when a
    /// group colour is close to the gold frame's).</summary>
    public static bool CloseTo(string a, string b)
    {
        var (l1, a1, b1) = Lab(a);
        var (l2, a2, b2) = Lab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2)) < CloseDistance;
    }

    static (double L, double A, double B) Lab(string hex)
    {
        double r = Linear(Convert.ToInt32(hex[1..3], 16)), g = Linear(Convert.ToInt32(hex[3..5], 16)), b = Linear(Convert.ToInt32(hex[5..7], 16));
        double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
        double fx = F((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047);
        double fy = F(0.2126 * r + 0.7152 * g + 0.0722 * b);
        double fz = F((0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    static double Linear(int v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
}
