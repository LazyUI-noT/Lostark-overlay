namespace GaugeOverlay.Core.Domain;

/// <summary>Round-number grids for the DPS graphs (DPS 미터기's 선그래프, 전투 기록's timeline): value lines at a round step with
/// the plot's top on a line, and time lines as fine as their labels allow. Pure.</summary>
public static class GridScale
{
    static readonly double[] Mantissas = [1, 2, 2.5, 5];
    static readonly double[] TimeSteps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200];
    const int MinLines = 3, MaxLines = 5;
    const double EmptyStep = 1e8;   // 1억: one line on a graph with nothing in it

    /// <summary>The step (1, 2, 2.5 or 5 × 10ⁿ) giving 3–5 lines from 0 to the top, the top being the first line at or above
    /// <paramref name="max"/>: of those, the one whose top sits closest above the peak (a tie: the larger step).</summary>
    public static (double Step, double Top) Values(double max)
    {
        if (!(max > 0)) return (EmptyStep, EmptyStep);
        int n = (int)Math.Floor(Math.Log10(max));
        var best = (Step: 0.0, Top: double.MaxValue);
        foreach (int p in new[] { n - 1, n })
            foreach (double m in Mantissas)
            {
                double step = m * Math.Pow(10, p);
                int lines = (int)Math.Ceiling(max / step - 1e-9);
                if (lines is < MinLines or > MaxLines) continue;
                double top = lines * step;
                if (top < best.Top - 1e-6 * step || Math.Abs(top - best.Top) <= 1e-6 * step && step > best.Step) best = (step, top);
            }
        return best.Step > 0 ? best : (max, max);   // unreachable for a positive max: some step always gives 3–5 lines
    }

    const double Pad = 1.2;        // the scale around a line: a fifth more than its swing
    const double MinHalf = 0.05;   // and at least 5 % of its middle each way, so a flat line's noise isn't blown up

    /// <summary>A scale centred between a line's lowest and highest value (그래프 범위 최근 1분, user 2026-11-11), so its
    /// swing fills the graph instead of lying flat on a scale from 0; never under 0. The step as <see cref="Values"/> picks
    /// it for the height shown.</summary>
    public static (double Step, double Bottom, double Top) Around(double lo, double hi)
    {
        double mid = (lo + hi) / 2, half = Math.Max((hi - lo) / 2 * Pad, mid * MinHalf);
        if (!(half > 0)) return (EmptyStep, 0, EmptyStep);
        double bottom = Math.Max(0, mid - half), top = mid + half;
        return (Values(top - bottom).Step, bottom, top);
    }

    /// <summary>The value lines of a scale: the round multiples of <paramref name="step"/> inside it, 0 left out (the plot's edge).</summary>
    public static IReadOnlyList<double> Lines(double step, double bottom, double top)
    {
        var lines = new List<double>();
        for (double k = Math.Max(1, Math.Ceiling(bottom / step - 1e-9)); k * step <= top * (1 + 1e-9); k++) lines.Add(k * step);
        return lines;
    }

    /// <summary>The finest time step (s) whose lines lie at least <paramref name="minGap"/> px apart across
    /// <paramref name="width"/> px showing <paramref name="span"/> s; the widest when none does.</summary>
    public static double Seconds(double span, double width, double minGap) =>
        TimeSteps.FirstOrDefault(s => span > 0 && width * s / span >= minGap, TimeSteps[^1]);

    /// <summary>The time lines inside (<paramref name="from"/>, <paramref name="from"/> + <paramref name="span"/>): round seconds of the
    /// fight, so a graph that doesn't start at 0 keeps its labels true.</summary>
    public static IEnumerable<double> Ticks(double from, double span, double step)
    {
        for (double t = (Math.Floor(from / step) + 1) * step; t < from + span; t += step) yield return t;
    }

    /// <summary>Whether a graph of <paramref name="height"/> × <paramref name="width"/> has room for its labels: four lines of
    /// <paramref name="font"/> tall, and the label column (<paramref name="labelWidth"/>) no more than a third of the width.
    /// Without it the graph draws its lines alone.</summary>
    public static bool LabelsFit(double height, double width, double font, double labelWidth) =>
        height >= 4 * font && labelWidth <= width / 3;

    /// <summary>The tops of a graph's end labels, for line ends <paramref name="ends"/> sorted top down: each
    /// <paramref name="lift"/> above its end, kept <paramref name="gap"/> apart, none above <paramref name="top"/> or lower than
    /// <paramref name="bottom"/> − <paramref name="lift"/>. Labels pushed past the bottom move up, those above with them, never above <paramref name="top"/> (too many to fit
    /// overlap there instead of covering what's above the plot).</summary>
    public static double[] Stack(IReadOnlyList<double> ends, double top, double bottom, double gap, double lift)
    {
        var y = new double[ends.Count];
        double lowest = bottom - lift;
        for (int i = 0; i < y.Length; i++) y[i] = Math.Max(i == 0 ? top : y[i - 1] + gap, Math.Min(ends[i] - lift, lowest));
        for (int i = y.Length - 1; i >= 0; i--) y[i] = Math.Max(top, Math.Min(y[i], i == y.Length - 1 ? lowest : y[i + 1] - gap));
        return y;
    }

    /// <summary>A DPS grid line's label, at most two decimals and none for a whole step:
    /// "5억", "2.5억", "0.5억": DPS always in 억, never 만 or 조 (user, 2026-11-12).</summary>
    public static string Label(double v) =>
        v == 0 ? "0" : (v / 1e8).ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture) + "억";
}
