using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>테스트 모드: the video picture inside a player window (영화 및 TV, YouTube, a Discord stream), so a window that isn't
/// fullscreen can stand in for the game's screen. Flat lines are trimmed from each edge (black bars, page margins, title and
/// control bars: nearly every pixel within the noise of the line's median); what is left is the picture when its shape is a
/// game's (16:9, 16:10, forced 21:9). Otherwise text under the video or thumbnails beside it are still inside, so the
/// pictures of those shapes that fit are offered, the ones inside the trimmed area first. The caller keeps the first one the
/// skill bar is found on. Pure; the coordinates are the image's.</summary>
public static class PictureFinder
{
    /// <summary>Share of a line's pixels within <see cref="FlatNoise"/> of its median for the line to be chrome (a bar, a margin),
    /// not picture. Measured on the full_* fixtures at ±5: a video's black bars ≥ 0.979 (Discord stream), the most even picture
    /// line ≤ 0.925 (1080p, the dark strip over the HP bar), so ±0.045 either side.</summary>
    public const double FlatShare = 0.97;

    /// <summary>Channel difference from a line's median still counted as the same colour: a compressed video's bars (1–3,
    /// Letterbox) and its blur.</summary>
    public const int FlatNoise = 5;

    /// <summary>How far a trimmed area's shape may be from a game's and still be taken as is.</summary>
    public const double AspectTolerance = 0.02;

    /// <summary>The game's shapes, most common first: 16:9, 16:10, the forced 21:9 (Letterbox: 1920 × 810).</summary>
    static readonly double[] Aspects = [16.0 / 9, 16.0 / 10, 64.0 / 27];

    /// <summary>The busy area must span this share of the window both ways: a smaller one is a dark scene's few lit parts or a
    /// tiny video (out of scope), and nothing is offered (the caller tries again later).</summary>
    public const double MinShare = 0.4;

    /// <summary>Pictures to try, best first: the busy area when it has a game's shape, then areas of each shape with its width
    /// (a hover bar or a dark sky makes its height the less sure one), then with its height. Empty for a blank window.</summary>
    public static IReadOnlyList<PixelRect> Candidates(Image img)
    {
        if (Busy(img) is not { } b || b.Width < MinShare * img.Width || b.Height < MinShare * img.Height) return [];
        var list = new List<PixelRect>();
        foreach (double a in Aspects)
            if (Math.Abs((double)b.Width / b.Height - a) <= a * AspectTolerance) list.Add(Snap(b, a));
        foreach (double a in Aspects) list.AddRange(ByWidth(b, a, img));
        foreach (double a in Aspects) list.AddRange(ByHeight(b, a, img));
        return [.. list.Distinct()];
    }

    /// <summary>The area <paramref name="a"/> gives the trimmed width, centred on it: the forced-21:9 rule of
    /// <see cref="Letterbox"/> (a video blurs the picture a row or two into its bars).</summary>
    static PixelRect Snap(PixelRect b, double a)
    {
        int h = (int)Math.Round(b.Width / a);
        return new(b.X, b.Y + (b.Height - h + 1) / 2, b.Width, h);   // half a row rounds down the picture, as Letterbox's bars do
    }

    /// <summary>Areas of shape <paramref name="a"/> with the busy width: at its top, centre, bottom, kept inside the image.</summary>
    static IEnumerable<PixelRect> ByWidth(PixelRect b, double a, Image img)
    {
        int h = (int)Math.Round(b.Width / a);
        if (h <= 0 || h > img.Height || h == b.Height) yield break;
        foreach (int y in new[] { b.Y, b.Y + (b.Height - h) / 2, b.Bottom - h })
            yield return new(b.X, Math.Clamp(y, 0, img.Height - h), b.Width, h);
    }

    /// <summary>Areas of shape <paramref name="a"/> with the busy height: at its left, centre, right, kept inside the image.</summary>
    static IEnumerable<PixelRect> ByHeight(PixelRect b, double a, Image img)
    {
        int w = (int)Math.Round(b.Height * a);
        if (w <= 0 || w > img.Width || w == b.Width) yield break;
        foreach (int x in new[] { b.X, b.X + (b.Width - w) / 2, b.Right - w })
            yield return new(Math.Clamp(x, 0, img.Width - w), b.Y, w, b.Height);
    }

    /// <summary>The picture's extent: the longest unbroken run of busy (not flat) rows, then of busy columns over those rows, and
    /// again until it settles. A title, a seek line or text under the video is a short run of its own, cut off by the flat
    /// lines around it; a picture is busy line after line. Null when no line is busy.</summary>
    internal static PixelRect? Busy(Image img)
    {
        var r = new PixelRect(0, 0, img.Width, img.Height);
        for (int pass = 0; pass < 4; pass++)
        {
            var rows = LongestRun(r.Y, r.Bottom, y => !Flat(img, r.X, y, 1, 0, r.Width));
            if (rows.Length == 0) return null;
            var cols = LongestRun(r.X, r.Right, x => !Flat(img, x, rows.Start, 0, 1, rows.Length));
            if (cols.Length == 0) return null;
            var next = new PixelRect(cols.Start, rows.Start, cols.Length, rows.Length);
            if (next == r) break;
            r = next;
        }
        return r;
    }

    static (int Start, int Length) LongestRun(int from, int to, Func<int, bool> busy)
    {
        (int Start, int Length) best = (from, 0);
        for (int i = from, start = from; i <= to; i++)
        {
            if (i < to && busy(i)) continue;
            if (i - start > best.Length) best = (start, i - start);
            start = i + 1;
        }
        return best;
    }

    /// <summary>Whether the <paramref name="n"/> pixels from (x, y) stepping (dx, dy) are chrome: at least <see cref="FlatShare"/>
    /// of them within <see cref="FlatNoise"/> of the line's median in every channel.</summary>
    static bool Flat(Image img, int x, int y, int dx, int dy, int n)
    {
        Span<int> hist = stackalloc int[3 * 256];
        var px = img.Bgra;
        int step = (dy * img.Width + dx) * 4, start = (y * img.Width + x) * 4;
        for (int i = 0, p = start; i < n; i++, p += step)
            for (int c = 0; c < 3; c++) hist[c * 256 + px[p + c]]++;
        Span<int> median = stackalloc int[3];
        for (int c = 0; c < 3; c++)
        {
            int seen = 0, v = 0;
            while ((seen += hist[c * 256 + v]) * 2 < n) v++;
            median[c] = v;
        }
        int near = 0;
        for (int i = 0, p = start; i < n; i++, p += step)
            if (Math.Abs(px[p] - median[0]) <= FlatNoise && Math.Abs(px[p + 1] - median[1]) <= FlatNoise && Math.Abs(px[p + 2] - median[2]) <= FlatNoise) near++;
        return near >= FlatShare * n;
    }
}
