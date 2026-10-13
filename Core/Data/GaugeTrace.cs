namespace GaugeOverlay.Core.Data;

/// <summary>A clip's gauge read at one frame: what <see cref="GaugeDef.Read"/> gave and what the engine would show after
/// <see cref="GaugeFilter"/>.</summary>
public sealed record TracePoint(TimeSpan At, double Raw, double Shown);

/// <summary>--trace-gauge: a submitted clip's gauge reads checked for effects that distort them (발키리's shine sweeping the orb,
/// 도화가's cast shine, or anything a new class draws). A read is flagged when it is more than <see cref="Jump"/> away from the
/// median of the <see cref="Half"/> reads on each side (the clip's first and last <see cref="Half"/> reads aren't judged): a short spike or dip, while a spend or a fill, which lasts, moves the
/// median with it. Flags on the raw reads show an effect; flags left on the shown values show one the filter doesn't absorb.</summary>
public static class GaugeTrace
{
    public const double Jump = 0.1;
    /// <summary>Reads on each side (0.5 s at 100 ms): an effect shorter than this is a jump, a longer one is the trend.</summary>
    public const int Half = 5;

    /// <summary>The reads through a fresh <see cref="GaugeFilter"/>, as the engine would (without key presses).</summary>
    public static IReadOnlyList<TracePoint> Run(IReadOnlyList<(TimeSpan At, double Raw)> reads)
    {
        var filter = new GaugeFilter();
        return [.. reads.Select(r => new TracePoint(r.At, r.Raw, filter.Add(r.Raw, r.At)))];
    }

    /// <summary>The indices of <paramref name="values"/> more than <paramref name="jump"/> from their window's median.</summary>
    public static IReadOnlyList<int> Flags(IReadOnlyList<double> values, double jump)
    {
        // ponytail: a sliding median, O(n·Half log Half); fine for a clip of a few thousand reads.
        var flags = new List<int>();
        // The first and last Half reads have no full window, so a spend there would look like a jump: not judged.
        for (int i = Half; i < values.Count - Half; i++)
        {
            int from = i - Half, to = i + Half;
            var window = values.Skip(from).Take(to - from + 1).Order().ToList();
            if (Math.Abs(values[i] - window[window.Count / 2]) > jump) flags.Add(i);
        }
        return flags;
    }
}
