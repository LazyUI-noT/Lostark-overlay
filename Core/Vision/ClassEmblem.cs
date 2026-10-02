using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>A class's centre piece of the skill bar (Geometry.RefEmblem: its 아덴 gauge art, which differs per class) from
/// two shots, gauge empty and full: only the pixels alike in both are compared, so the gauge's fill and the scene behind
/// the art's gaps leave the match alone (ClassEmblemMatcher).</summary>
public sealed class ClassEmblem
{
    // A pixel whose channels differ more than this between the two shots is fill or scene, not the frame.
    const int MaxStaticDiff = 30;
    // Columns next to the 아덴 slots either side, left out: a ready 아덴 skill's glow spills over them (4k_down.mp4).
    const int SideMargin = 10;

    public string Class { get; }
    public int Width { get; }
    public int Height { get; }
    internal float[] Grey { get; }
    internal bool[] Static { get; }
    readonly Dictionary<(int, int), float[]> _blurred = [];

    public ClassEmblem(string cls, Image empty, Image full)
    {
        if (empty.Width != full.Width || empty.Height != full.Height) throw new ArgumentException("the two shots differ in size");
        (Class, Width, Height) = (cls, empty.Width, empty.Height);
        Grey = new float[Width * Height];
        Static = new bool[Width * Height];
        for (int i = 0; i < Grey.Length; i++)
        {
            int p = i * 4;
            int diff = 0;
            for (int c = 0; c < 3; c++) diff = Math.Max(diff, Math.Abs(empty.Bgra[p + c] - full.Bgra[p + c]));
            int x = i % Width;
            Static[i] = diff <= MaxStaticDiff && x >= SideMargin && x < Width - SideMargin;
            Grey[i] = (ClassEmblemMatcher.Luma(empty.Bgra, p) + ClassEmblemMatcher.Luma(full.Bgra, p)) / 2;
        }
    }

    /// <summary>The grey as a crop of <paramref name="w"/>×<paramref name="h"/> would show it: shrunk to that size and grown
    /// back, so a crop from a smaller skill bar (4K downscaled, a low HUD) meets its own sharpness. Kept per size.</summary>
    internal float[] GreyAt(int w, int h)
    {
        if (w >= Width && h >= Height) return Grey;
        lock (_blurred)
        {
            if (!_blurred.TryGetValue((w, h), out var g))
                _blurred[(w, h)] = g = ClassEmblemMatcher.Resample(ClassEmblemMatcher.Resample(Grey, Width, Height, w, h), w, h, Width, Height);
            return g;
        }
    }
}

/// <summary>Which class a skill bar's centre piece shows (<see cref="ClassEmblem"/>). Heavier than EmblemMatcher (full
/// resolution, a few pixels of fit error searched), so it runs once per capture: after a fit or a loading screen.</summary>
public static class ClassEmblemMatcher
{
    // Fit error searched, in reference pixels either way.
    const int Shift = 2;
    // Another class is told only this far above the class set. On every recorded frame the class played scores highest,
    // by 0.03 at the least (a tooltip over 바드's blurred 4K harp) and 0.17 without one (ClassEmblemTests).
    const double MinLead = 0.1;

    /// <summary>The class <paramref name="crop"/> shows when it is not <paramref name="set"/>: another class's frame scoring
    /// <see cref="MinLead"/> above every one of <paramref name="set"/>'s. Null when it is <paramref name="set"/>, when that
    /// is unclear, or when <paramref name="set"/> has no reference (a class whose gauge isn't tracked).</summary>
    public static string? Other(Image crop, string set, IReadOnlyList<ClassEmblem> refs)
    {
        if (!refs.Any(r => r.Class == set)) return null;
        var best = refs.GroupBy(r => r.Class).ToDictionary(g => g.Key, g => g.Max(r => Score(crop, r)));
        var (other, score) = best.Where(b => b.Key != set).MaxBy(b => b.Value);
        return score >= best[set] + MinLead ? other : null;
    }

    // The class played scores 0.55 and up on idle frames, another 0.52 at most (ClassEmblemShownTests).
    const double MinShown = 0.6;

    /// <summary>The class <paramref name="crop"/> clearly shows with nothing to compare it against: its frame scores <see
    /// cref="MinShown"/> or more and <see cref="MinLead"/> above every other class's. Null when unclear or a class without
    /// a reference.</summary>
    public static string? Shown(Image crop, IReadOnlyList<ClassEmblem> refs)
    {
        var best = refs.GroupBy(r => r.Class).Select(g => (Class: g.Key, Score: g.Max(r => Score(crop, r))))
            .OrderByDescending(b => b.Score).ToList();
        if (best.Count == 0 || best[0].Score < MinShown) return null;
        return best.Count == 1 || best[0].Score >= best[1].Score + MinLead ? best[0].Class : null;
    }


    internal static float Luma(byte[] bgra, int p) => 0.114f * bgra[p] + 0.587f * bgra[p + 1] + 0.299f * bgra[p + 2];

    /// <summary>How alike <paramref name="crop"/> (any size; scaled to the reference) is to <paramref name="emblem"/>'s
    /// frame: the best correlation over the searched shifts, 0..1.</summary>
    public static double Score(Image crop, ClassEmblem emblem)
    {
        if (crop.Width == 0 || crop.Height == 0) return 0;   // a capture clipped off screen
        int w = emblem.Width, h = emblem.Height;
        var grey = Resample(Luma(crop), crop.Width, crop.Height, w, h);
        var refGrey = emblem.GreyAt(crop.Width, crop.Height);
        double best = 0;
        for (int dy = -Shift; dy <= Shift; dy++)
            for (int dx = -Shift; dx <= Shift; dx++)
            {
                double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
                int n = 0;
                for (int y = Math.Max(0, -dy); y < Math.Min(h, h - dy); y++)
                    for (int x = Math.Max(0, -dx); x < Math.Min(w, w - dx); x++)
                    {
                        int i = y * w + x;
                        if (!emblem.Static[i]) continue;
                        double a = refGrey[i], b = grey[(y + dy) * w + x + dx];
                        (sa, sb, saa, sbb, sab, n) = (sa + a, sb + b, saa + a * a, sbb + b * b, sab + a * b, n + 1);
                    }
                if (n == 0) continue;
                double va = saa - sa * sa / n, vb = sbb - sb * sb / n;
                if (va > 1e-6 && vb > 1e-6) best = Math.Max(best, (sab - sa * sb / n) / Math.Sqrt(va * vb));
            }
        return best;
    }

    static float[] Luma(Image img)
    {
        var g = new float[img.Width * img.Height];
        for (int i = 0; i < g.Length; i++) g[i] = Luma(img.Bgra, i * 4);
        return g;
    }

    // Bilinear resample of a <paramref name="sw"/>×<paramref name="sh"/> grey image to w×h.
    internal static float[] Resample(float[] src, int sw, int sh, int w, int h)
    {
        var dst = new float[w * h];
        double kx = (double)sw / w, ky = (double)sh / h;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double fx = Math.Clamp((x + 0.5) * kx - 0.5, 0, sw - 1), fy = Math.Clamp((y + 0.5) * ky - 0.5, 0, sh - 1);
                int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
                double tx = fx - x0, ty = fy - y0;
                float L(int px, int py) => src[py * sw + px];
                dst[y * w + x] = (float)((L(x0, y0) * (1 - tx) + L(x1, y0) * tx) * (1 - ty) + (L(x0, y1) * (1 - tx) + L(x1, y1) * tx) * ty);
            }
        return dst;
    }
}
