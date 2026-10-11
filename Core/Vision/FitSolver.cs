using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Finds the HUD scale from the HP bar's top edge and a small nudge from the gold 각성기 frame, both the same for every
/// class (PLAN §1). The frame alone fooled the size: 바드's charged frame matched worse than the slot beside it (bardbug_103.mp4).
/// Only the frame ring is compared; the class-specific icon inside, the light that runs around it
/// and the see-through panel are ignored (see FrameTemplate).</summary>
public static class FitSolver
{
    private const double Good = 0.6;       // a frame match this good places the bar; below, the size stands without a nudge
    private const double MinScore = 0.4;   // below this there is no 각성기 frame at the size read: no skill bar in the band
    private const int Radius = 8;          // nudge search, in screen pixels around the predicted spot
    private const int WideRadius = 40;     // 직접 입력's second search, in pixels at 1080p (ManualFitTests: a bar 30 px off is found)

    // The HP bar's top row over its left end (filled until HP is low, and clear of the HP numbers), at reference scale (HUD 110, 1080 high):
    // the same for every class, 1.21 × HUD px above the bottom (80percent–110percent.png: rows 983, 971, 953, 947).
    private static readonly PixelRect RefHpBar = new(590, 947, 60, 1);
    private const double MinHpEdge = 1.2;  // of 2: the red share of the edge row and the one below, less the two above
    private const double EdgeTie = 0.15;   // rows scoring this close to the best are the same blurred edge
    private const int MinHpWidth = 8;      // pixels of the row the capture must hold

    /// <summary>The area to capture for <see cref="Solve"/>: the bottom 40% of the game area, which contains the
    /// HP bar and the 각성기 frame at every HUD size.</summary>
    public static PixelRect Band(Viewport vp)
    {
        int top = vp.Height * 3 / 5;
        return new(vp.X, vp.Y + top, vp.Width, vp.Height - top);
    }

    /// <summary>Fits the skill bar in <paramref name="bottomBand"/>, a capture of <paramref name="bandRect"/>
    /// (screen pixels) that contains the HP bar and the 각성기 frame. The HP bar gives the HUD size, or the user does
    /// (<paramref name="hud"/>, 크기 직접 입력); the frame only gives the nudge. Null when the HP bar isn't found, or no frame is at
    /// the size it gives.</summary>
    /// <param name="video">테스트 모드 on a stream or video: compression blurs the frame's thin ring below <see cref="MinScore"/>
    /// (0.23–0.35 on a Discord stream where the bar sat within 3 px of the HP-bar spot), so the HP bar alone gives the size.</param>
    public static Fit? Solve(Image bottomBand, PixelRect bandRect, Viewport vp, bool video = false)
    {
        if (HudFromHpBar(bottomBand, bandRect, vp) is not { } size) return null;
        var m = Match(bottomBand, bandRect, vp, size, Radius);
        if (m.Score < MinScore) return video ? new Fit(size) : null;   // a red edge with no skill bar under it: not the HUD
        if (m.Score < Good) return new Fit(size);   // a charged or covered frame places nothing
        double k = Geometry.Scale(vp, new Fit(size));
        return new Fit(size, m.Dx / k, m.Dy / k);
    }

    /// <summary>A 직접 입력 fit: <see cref="Fit"/> at the typed HUD size, or null when the bar isn't where the typed values put it;
    /// <see cref="Moved"/>, the screen pixels the wider search moved it (0: where they said).</summary>
    public sealed record ManualResult(Fit? Fit, int Moved);

    /// <summary>직접 입력: nothing is read off the screen but whether the bar is there. The 각성기 frame at the typed size near the
    /// spot the typed values give, with the HP bar's top edge beside it, places it; failing that, the frame further off (a position or
    /// ratio error). A charged or covered frame places nothing well, so there a weak frame match near the spot (as <see cref="Solve"/>
    /// takes for a skill bar) with the HP bar edge at the very spot keeps the typed size; the edge alone doesn't (a bar off sideways).</summary>
    public static ManualResult SolveManual(Image bottomBand, PixelRect bandRect, Viewport vp, double hud)
    {
        double k = Geometry.Scale(vp, new Fit(hud));
        var near = Match(bottomBand, bandRect, vp, hud, Radius);
        if (near.Score >= Good && HpEdgeAt(bottomBand, bandRect, vp, hud, near.Dx, near.Dy)) return new(new Fit(hud, near.Dx / k, near.Dy / k), 0);
        var far = Match(bottomBand, bandRect, vp, hud, (int)Math.Round(WideRadius * vp.Height / Geometry.ReferenceHeight));
        if (far.Score >= Good && HpEdgeAt(bottomBand, bandRect, vp, hud, far.Dx, far.Dy))
            return new(new Fit(hud, far.Dx / k, far.Dy / k), Math.Max(Math.Abs(far.Dx), Math.Abs(far.Dy)));
        return new(near.Score >= MinScore && HpEdgeAt(bottomBand, bandRect, vp, hud, 0, 0) ? new Fit(hud) : null, 0);
    }

    /// <summary>The HP bar's top edge at the row <paramref name="hud"/> puts it (±1), moved by the frame's offset.</summary>
    private static bool HpEdgeAt(Image band, PixelRect bandRect, Viewport vp, double hud, int dx, int dy)
    {
        var r = Geometry.ToScreen(RefHpBar, vp, new Fit(hud));
        for (int y = r.Y + dy - 1; y <= r.Y + dy + 1; y++)
        {
            var s = Enumerable.Range(y - 2, 4).Select(row => RedShare(band, bandRect, r.X + dx, r.Right + dx, row)).ToArray();
            if (s.All(v => v >= 0) && s[2] + s[3] - s[0] - s[1] >= MinHpEdge) return true;
        }
        return false;
    }

    /// <summary>The HUD size from the HP bar's top edge: the row where its filled left end turns red. A row is scored by
    /// the red share of it and the row below minus the two above, so a blurred edge (video) ties two rows and lands between
    /// them; then <see cref="EdgeRow"/> places it to a fraction of a row. Null if no row looks like it.</summary>
    public static double? HudFromHpBar(Image band, PixelRect bandRect, Viewport vp)
    {
        var edges = new List<(double Hud, int Row, double Edge)>();
        for (double h = 50; h <= 250; h += 0.1)
        {
            var r = Geometry.ToScreen(RefHpBar, vp, new Fit(h));
            var s = Enumerable.Range(r.Y - 2, 4).Select(y => RedShare(band, bandRect, r.X, r.Right, y)).ToArray();
            if (s.All(v => v >= 0)) edges.Add((h, r.Y, s[2] + s[3] - s[0] - s[1]));
        }
        if (edges.Count == 0) return null;
        var top = edges.MaxBy(e => e.Edge);
        if (top.Edge < MinHpEdge) return null;
        double coarse = edges.Where(e => Math.Abs(e.Row - top.Row) <= 1 && e.Edge >= top.Edge - EdgeTie).Average(e => e.Hud);
        return EdgeRow(band, bandRect, vp, coarse) is { } row
            ? (vp.Y + vp.Height - row) / (Geometry.ReferenceHeight - RefHpBar.Y) * Geometry.ReferenceHud / (vp.Height / Geometry.ReferenceHeight)
            : coarse;
    }

    /// <summary>The HP bar's top row to a fraction of a pixel: where the mean red of the rows crosses half-way from the border
    /// (the darkest of the rows around the edge) to two rows inside. A video smears the colour a row upwards (the border row turns half red), but the
    /// crossing stays put: 946.4 between the border and the first bar row in 110percent.png and in 333.mp4 alike.</summary>
    private static double? EdgeRow(Image band, PixelRect bandRect, Viewport vp, double hud)
    {
        var r = Geometry.ToScreen(RefHpBar, vp, new Fit(hud));
        var red = Enumerable.Range(r.Y - 2, 5).Select(y => MeanRed(band, bandRect, r.X, r.Right, y)).ToArray();
        if (red.Any(v => v < 0)) return null;
        // The darkest row is the bar's border: gold UI can sit just above it at small sizes, and the coarse row can be the border itself.
        int above = Enumerable.Range(0, 3).MinBy(i => red[i]);
        double half = (red[above] + red[4]) / 2;
        for (int i = above; i < 4; i++)
            if (red[i] < half && red[i + 1] >= half)
                return r.Y - 2 + i + (half - red[i]) / (red[i + 1] - red[i]) + 0.5;   // the boundary is half a row above the row
        return null;
    }

    private static double MeanRed(Image band, PixelRect bandRect, int x0, int x1, int y)
    {
        int by = y - bandRect.Y, from = Math.Max(0, x0 - bandRect.X), to = Math.Min(band.Width, x1 - bandRect.X);
        if (by < 0 || by >= band.Height || to - from < MinHpWidth) return -1;
        double sum = 0;
        for (int x = from; x < to; x++) sum += band.Pixel(x, by).R;
        return sum / (to - from);
    }

    /// <summary>Share of the pixels in screen row <paramref name="y"/>, columns x0..x1, that are the HP bar's red; -1 when
    /// the row or too little of it is in the capture.</summary>
    private static double RedShare(Image band, PixelRect bandRect, int x0, int x1, int y)
    {
        int by = y - bandRect.Y, from = Math.Max(0, x0 - bandRect.X), to = Math.Min(band.Width, x1 - bandRect.X);
        if (by < 0 || by >= band.Height || to - from < MinHpWidth) return -1;
        int red = 0;
        for (int x = from; x < to; x++)
        {
            var (b, g, r) = band.Pixel(x, by);
            if (r >= 90 && r > 2 * g && r > 2 * b) red++;
        }
        return red / (double)(to - from);
    }

    private readonly record struct Candidate(double Hud, int Dx, int Dy, double Score);

    private static Candidate Match(Image band, PixelRect bandRect, Viewport vp, double hud, int radius)
    {
        var fit = new Fit(hud);
        var r = Geometry.ToScreen(Geometry.RefAwakeningFrame, vp, fit);
        if (r.Width < 8) return new(hud, 0, 0, -1);
        var (t, mask) = Template(r.Width, r.Height);
        var best = new Candidate(hud, 0, 0, -1);
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x0 = r.X + dx - bandRect.X, y0 = r.Y + dy - bandRect.Y;
                if (x0 < 0 || y0 < 0 || x0 + r.Width > band.Width || y0 + r.Height > band.Height) continue;
                double s = Ncc(band, x0, y0, r.Width, r.Height, t, mask);
                if (s > best.Score) best = new(hud, dx, dy, s);
            }
        return best;
    }

    /// <summary>The frame template resized to w×h: grey values and a mask of the ring pixels.</summary>
    private static (float[] Grey, bool[] Mask) Template(int w, int h)
    {
        var rows = FrameTemplate.Rows;
        int sw = rows[0].Length, sh = rows.Length;
        var grey = new float[w * h];
        var mask = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Nearest cell: the game draws the frame's thin lines crisp at every size, so averaging would blur them.
                char c = rows[Math.Min(sh - 1, (int)((y + 0.5) * sh / h))][Math.Min(sw - 1, (int)((x + 0.5) * sw / w))];
                grey[y * w + x] = c == '?' ? 0 : DigitReader.GreyRamp.IndexOf(c) * 28.3f;
                mask[y * w + x] = c != '?';
            }
        return (grey, mask);
    }

    private static double Ncc(Image img, int x0, int y0, int w, int h, float[] t, bool[] mask)
    {
        double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
        int n = 0;
        var px = img.Bgra;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = y * w + x;
                if (!mask[k]) continue;
                int i = (y0 + y) * img.Stride + (x0 + x) * 4;
                double a = (px[i] + px[i + 1] + px[i + 2]) / 3.0, b = t[k];
                sa += a; sb += b; saa += a * a; sbb += b * b; sab += a * b;
                n++;
            }
        double va = saa - sa * sa / n, vb = sbb - sb * sb / n;
        return va <= 1e-6 || vb <= 1e-6 ? 0 : (sab - sa * sb / n) / Math.Sqrt(va * vb);
    }
}
