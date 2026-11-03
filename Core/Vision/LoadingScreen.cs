using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The game's loading screen, by its bar along the bottom (strip: <see cref="Geometry.LoadingBar"/>): a thin grey
/// track that fills with blue from the left (a glowing head, a white shimmer), on black. Measured on v4, 지옥 and the two
/// 21:9 clips: loading frames give 100% of the columns on the track and 98–100% black around it; every other frame (the
/// game, menus, the taskbar over a loading) under 1% black around it.</summary>
public static class LoadingScreen
{
    const int Columns = 121;                 // sampled across the track
    const double TrackFrom = 15, TrackTo = 1805, Gap = 8;   // ref px in the strip: the track's ends, the black rows' distance
    const double OnTrack = 0.9, Black = 0.85;

    public static bool Is(Image strip, double scale)
    {
        if (strip.Width == 0 || strip.Height == 0) return false;
        var xs = Enumerable.Range(0, Columns)
            .Select(i => Math.Min(strip.Width - 1, (int)Math.Round((TrackFrom + i * (TrackTo - TrackFrom) / (Columns - 1)) * scale)))
            .ToArray();
        int best = -1, bestCount = 0;
        for (int y = 0; y < strip.Height; y++)
        {
            int n = xs.Count(x => Track(strip.Pixel(x, y)));
            if (n > bestCount) (best, bestCount) = (y, n);
        }
        if (bestCount < OnTrack * Columns) return false;
        int gap = (int)Math.Round(Gap * scale);
        return Dark(strip, xs, best - gap) >= Black * Columns && Dark(strip, xs, best + gap) >= Black * Columns;
    }

    /// <summary>The track's grey, the blue fill or the white shimmer.</summary>
    static bool Track((byte B, byte G, byte R) p)
    {
        int lo = Math.Min(p.R, Math.Min(p.G, p.B)), hi = Math.Max(p.R, Math.Max(p.G, p.B));
        bool grey = p.R is >= 12 and <= 80 && p.B is >= 12 and <= 90 && hi - lo <= 22;
        return grey || Blue(p) || lo >= 150;
    }

    static bool Blue((byte B, byte G, byte R) p) => p.B >= 80 && p.B > p.R + 30;

    /// <summary>Columns black (or the fill's blue glow) on row <paramref name="y"/>; none when it is off the strip.</summary>
    static int Dark(Image strip, int[] xs, int y) =>
        y < 0 || y >= strip.Height ? 0 : xs.Count(x => strip.Pixel(x, y) is var p && (p.R + p.G + p.B < 45 || Blue(p)));
}
