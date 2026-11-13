namespace GaugeOverlay.Core.Domain;

/// <summary>One delay's samples over the [perf] minute as "median/95%/max" ms (nearest-rank percentiles). Pure.</summary>
public static class DelayStats
{
    public static (double Median, double P95, double Max) Of(IReadOnlyList<double> ms)
    {
        if (ms.Count == 0) return (0, 0, 0);
        var sorted = ms.Order().ToArray();
        return (Rank(sorted, 0.5), Rank(sorted, 0.95), sorted[^1]);
    }

    public static string Text(IReadOnlyList<double> ms)
    {
        var (median, p95, max) = Of(ms);
        return $"{median:0.0}/{p95:0.0}/{max:0.0}";
    }

    static double Rank(double[] sorted, double p) => sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Length) - 1)];
}
