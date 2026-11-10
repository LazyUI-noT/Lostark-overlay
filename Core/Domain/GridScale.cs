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

    /// <summary>A grid line's label in the game's units (조, 억 or 만) with at most one decimal and none for a whole step:
    /// "5억", "2.5억", "1조".</summary>
    public static string Label(double v) =>
        v >= 1e12 ? $"{v / 1e12:0.##}조" : v >= 1e8 ? $"{v / 1e8:0.##}억" : v >= 1e4 ? $"{v / 1e4:0.##}만" : $"{v:0}";
}
