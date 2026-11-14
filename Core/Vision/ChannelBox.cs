using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The channel box at the top right (corner: <see cref="Geometry.ChannelCorner"/>), left of the minimap's − button. Its
/// drop-down arrow is gold in town and field (the box names the channel), grey in instanced content (the box shows dashes),
/// so a gold arrow means outside any dungeon (channel-cue.plan.md). Gold is read as bright and warm (blue well under red and
/// green): one 21:9 recording shifts it greener (210, 218, 182) where the others give (220, 204, 148); the grey arrow is
/// neutral and under 130. Measured on chan_* (1080, 21:9, 4K, 4K forced 21:9): 25–32 warm pixels at 1080 scale in town, 0
/// everywhere else (a whole raid, loading screens, a white flash). Says nothing when unsure: a missing box is no dungeon.</summary>
public static class ChannelBox
{
    // The arrow's window in the corner, in 1080 px from the corner's right edge and top: it sits 42–55 px in, a few px apart
    // between recordings.
    const double FromRight = 72, ToRight = 35, Top = 8, Bottom = 32;
    const int MinWarm = 12, MaxWarm = 60;                 // at 1080 scale: town 25–32, the rest 0
    const double MinSpan = 6, MaxSpan = 16;               // the warm part's width and height at 1080 scale: 10–11 × 8
    // The warm pixels fill a solid triangle: 0.31–0.38 of their bounding box on every chan_* town frame. Scattered warm
    // pixels (sparks, a gold effect, torchlight behind the semi-transparent box) fill far less: 30 over 16 × 16 is 0.12.
    const double MinFill = 0.2;
    /// <summary>A gap in the reads longer than this (HUD hidden, a loading, the game in the background) starts the count
    /// over: two gold reads in a row means two reads a second apart, not two with nothing grey between.</summary>
    public static readonly TimeSpan ReadGap = TimeSpan.FromSeconds(2.5);

    public static bool IsOutside(Image corner, double scale)
    {
        if (corner.Width == 0 || corner.Height == 0 || scale <= 0) return false;
        int x0 = Math.Max(0, corner.Width - (int)Math.Round(FromRight * scale)), x1 = Math.Max(x0, corner.Width - (int)Math.Round(ToRight * scale));
        int y0 = Math.Min(corner.Height, (int)Math.Round(Top * scale)), y1 = Math.Min(corner.Height, (int)Math.Round(Bottom * scale));
        int n = 0, left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                if (!Warm(corner.Pixel(x, y))) continue;
                n++;
                (left, right, top, bottom) = (Math.Min(left, x), Math.Max(right, x), Math.Min(top, y), Math.Max(bottom, y));
            }
        if (n == 0) return false;
        double area = scale * scale, w = (right - left + 1) / scale, h = (bottom - top + 1) / scale;
        double fill = n / (double)((right - left + 1) * (bottom - top + 1));
        return n >= MinWarm * area && n <= MaxWarm * area && Within(w) && Within(h) && fill >= MinFill;
    }

    /// <summary>Gold reads in a row after this one (<paramref name="last"/>: the previous read, null for none): a grey read or
    /// a gap over <see cref="ReadGap"/> starts over.</summary>
    public static int Hits(int hits, TimeSpan? last, bool outside, TimeSpan t) =>
        !outside ? 0 : last is { } l && t - l <= ReadGap ? hits + 1 : 1;

    static bool Within(double span) => span is >= MinSpan and <= MaxSpan;

    /// <summary>The gold arrow's pixels: bright red and green, blue well under both.</summary>
    static bool Warm((byte B, byte G, byte R) p) => Math.Min(p.R, p.G) >= 140 && Math.Min(p.R, p.G) - p.B >= 20;
}
