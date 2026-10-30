using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>테스트 모드's fine-tune. The HP bar's top edge already gives a size between the game's steps (<see cref="HudFromHpBar"/>),
/// but on a video the 각성기 ring is often too blurred to place the bar, and <see cref="Solve"/> then leaves it where the size alone
/// puts it. The HP bar's left end, which blur moves no more than the top edge, places it sideways instead. The same cues score a
/// fit for the adjust layer's green.</summary>
public static partial class FitSolver
{
    // The HP bar's left end, HpLeftRows reference rows below its top edge (the end slants, so one row), in reference x, as EndX
    // finds it: 588.7–591.2 on every game shot (band_80 … band_4k219, band_greyed, band_buff, band_b80), so ±1.2 px at 1080p;
    // 594.1 on the Discord stream, whose bar sits ~2.6 screen px right of where its size puts it.
    private const double RefHpLeft = 589.8, HpLeftRows = 4;
    private const double LeftTent = 3;     // reference px off at which the left end counts nothing (its spread is ±1.2)
    private const double MinRedness = 30;   // red less the brighter of green and blue: the end's contrast must reach this
    // Score at the fit Refine finds: 1.56–2.88 on every game shot (b80 lowest, its top edge weak) and 1.60 on the Discord stream.
    // Off it: 0–1.07 3 px up or down and 0–0.93 6 px off on all of them, so ReadsWell takes 1.3 (margins 0.26, 0.23, 0.37).
    // 3 px sideways still scores 0.59–1.88 (2.34 at 4K, where 3 px is 1.6 reference px): a bar 2–3 px off sideways can show green,
    // which the slots' reads tolerate.
    private const double ScoreGood = 1.3;

    /// <summary>Whether a bar scoring <paramref name="score"/> reads well (the adjust layer draws it green).</summary>
    public static bool ReadsWell(double score) => score >= ScoreGood;

    /// <summary>The bar's score at exactly <paramref name="fit"/> (its nudge included, no search): the HP bar's top edge (0–1) and
    /// left end (0–1), plus the 각성기 frame's match when it reaches <see cref="MinScore"/> (a blurred one counts nothing). Higher is better.</summary>
    public static double Score(Image band, PixelRect bandRect, Viewport vp, Fit fit)
    {
        var bare = new Fit(fit.Hud);
        double k = Geometry.Scale(vp, bare);
        var frame = Geometry.ToScreen(Geometry.RefAwakeningFrame, vp, bare);
        if (frame.Width < 8) return 0;
        var (t, mask) = Template(frame.Width, frame.Height);
        int dx = (int)Math.Round(fit.NudgeX * k), dy = (int)Math.Round(fit.NudgeY * k);
        int x0 = frame.X + dx - bandRect.X, y0 = frame.Y + dy - bandRect.Y;
        double f = x0 < 0 || y0 < 0 || x0 + frame.Width > band.Width || y0 + frame.Height > band.Height ? 0 : Ncc(band, x0, y0, frame.Width, frame.Height, t, mask);
        return (f >= MinScore ? f : 0) + HpEdge(band, bandRect, vp, bare, dx, dy) + HpLeft(band, bandRect, vp, bare, fit.NudgeX * k, dy);
    }

    /// <summary>The bar at <paramref name="start"/>'s HUD size, placed anew: by the 각성기 frame when it matches as well as
    /// <see cref="Solve"/> trusts (<see cref="Good"/>), else sideways by the HP bar's left end (up and down the top edge already
    /// set with the size). Null when neither is found: a red edge with no skill bar under it. <paramref name="start"/>'s row shifts
    /// and boss size are kept. A video moved up or down a pixel reads as a size ~1 % off: blur leaves no second cue.</summary>
    public static Fit? Refine(Image band, PixelRect bandRect, Viewport vp, Fit start)
    {
        var bare = new Fit(start.Hud);
        double k = Geometry.Scale(vp, bare);
        var m = Match(band, bandRect, vp, start.Hud, NearRadius(vp));
        if (m.Score >= Good) return start with { NudgeX = m.Dx / k, NudgeY = m.Dy / k };
        var (y, x) = LeftSpot(vp, bare, 0);
        return EndX(band, bandRect, y, x, NearRadius(vp)) is { } end ? start with { NudgeX = (end - x) / k, NudgeY = 0 } : null;
    }

    /// <summary>The screen row the HP bar's left end is read in, and the x <paramref name="bare"/> moved <paramref name="dy"/> puts it.</summary>
    private static (int Y, double X) LeftSpot(Viewport vp, Fit bare, int dy)
    {
        double k = Geometry.Scale(vp, bare);
        return (Geometry.ToScreen(RefHpBar, vp, bare).Y + dy + (int)Math.Round(HpLeftRows * k), vp.X + vp.Width / 2.0 + (RefHpLeft - 960) * k);
    }

    /// <summary>The HP bar's left end, 0–1: 1 where the fit (moved <paramref name="dx"/> screen px) puts it, falling to 0 at 1.5
    /// reference px off.</summary>
    private static double HpLeft(Image band, PixelRect bandRect, Viewport vp, Fit bare, double dx, int dy)
    {
        double k = Geometry.Scale(vp, bare);
        var (y, x) = LeftSpot(vp, bare, dy);
        x += dx;
        return EndX(band, bandRect, y, x, Math.Max(4, (int)Math.Round(4 * k))) is { } end ? Math.Max(0, 1 - Math.Abs(end - x) / (LeftTent * k)) : 0;
    }

    /// <summary>The HP bar's left end in screen row <paramref name="y"/>, to a fraction of a pixel: where the redness (over the row
    /// and the two beside it) crosses half-way from the darkest to the reddest column, the crossing nearest <paramref name="near"/>
    /// within <paramref name="radius"/>; null with too little contrast or outside the capture.</summary>
    internal static double? EndX(Image band, PixelRect bandRect, int y, double near, int radius)
    {
        int from = (int)Math.Floor(near) - radius - 2 - bandRect.X, to = (int)Math.Ceiling(near) + radius + 2 - bandRect.X, by = y - bandRect.Y;
        if (from < 0 || to >= band.Width || by < 1 || by + 1 >= band.Height) return null;
        var red = new double[to - from + 1];
        for (int i = 0; i < red.Length; i++)
            for (int row = by - 1; row <= by + 1; row++)
            {
                var (b, g, r) = band.Pixel(from + i, row);
                red[i] += Math.Max(0, r - Math.Max(g, b)) / 3.0;
            }
        double lo = red.Min(), hi = red.Max(), half = (lo + hi) / 2;
        if (hi - lo < MinRedness) return null;
        double? best = null;
        for (int i = 0; i + 1 < red.Length; i++)
            if (red[i] < half && red[i + 1] >= half)
            {
                double x = bandRect.X + from + i + (half - red[i]) / (red[i + 1] - red[i]) + 0.5;   // the boundary is half a column right
                if (best is null || Math.Abs(x - near) < Math.Abs(best.Value - near)) best = x;
            }
        return best;
    }

    /// <summary>The HP bar's top edge at the row the fit puts it, 0–1: the red share of the two rows below less the two above, halved.</summary>
    private static double HpEdge(Image band, PixelRect bandRect, Viewport vp, Fit bare, int dx, int dy)
    {
        var r = Geometry.ToScreen(RefHpBar, vp, bare);
        int y = r.Y + dy;
        var s = Enumerable.Range(y - 2, 4).Select(row => RedShare(band, bandRect, r.X + dx, r.Right + dx, row)).ToArray();
        return s.Any(v => v < 0) ? 0 : Math.Max(0, (s[2] + s[3] - s[0] - s[1]) / 2);
    }
}
