using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Tests;

/// <summary>--trace-gauge: a clip's gauge reads, one per 100 ms like the engine, checked for effects (a glow, a shine sweeping
/// the gauge, a cast flash) that make a read jump against the clip's own trend. A spend is a lasting step, not a jump.</summary>
public class GaugeTraceTests
{
    static IReadOnlyList<(TimeSpan, double)> Reads(params double[] v) => [.. v.Select((x, i) => (TimeSpan.FromMilliseconds(100 * i), x))];

    [Fact]
    public void A_short_dip_against_a_steady_gauge_is_flagged_and_a_steady_gauge_is_not()
    {
        double[] v = [.. Enumerable.Repeat(0.6, 10), 0.2, 0.2, 0.2, .. Enumerable.Repeat(0.6, 10)];   // a shine reads low for 3 reads
        var flags = GaugeTrace.Flags([.. v], GaugeTrace.Jump);
        Assert.Equal([10, 11, 12], flags);
        Assert.Empty(GaugeTrace.Flags([.. Enumerable.Repeat(0.6, 23)], GaugeTrace.Jump));
    }

    [Fact]
    public void A_spend_is_a_step_and_is_not_flagged()
    {
        double[] v = [.. Enumerable.Repeat(0.9, 12), .. Enumerable.Repeat(0.3, 12)];
        Assert.Empty(GaugeTrace.Flags(v, GaugeTrace.Jump));
    }

    [Fact]
    public void A_spend_right_at_the_start_or_end_of_the_clip_is_not_flagged()
    {
        Assert.Empty(GaugeTrace.Flags([1, 1, .2, .2, .2, .2, .2, .2, .2, .2, .2, .2], GaugeTrace.Jump));
        Assert.Empty(GaugeTrace.Flags([.9, .9, .9, .9, .9, .9, .9, .9, .9, .9, .3, .3], GaugeTrace.Jump));
    }

    [Fact]
    public void The_run_keeps_the_raw_read_and_what_the_engine_would_show()
    {
        double[] v = [.. Enumerable.Repeat(0.6, 10), 0.2, 0.2, 0.2, .. Enumerable.Repeat(0.6, 10)];
        var points = GaugeTrace.Run(Reads(v));
        Assert.Equal(v, points.Select(p => p.Raw));
        Assert.All(points.Skip(5), p => Assert.Equal(0.6, p.Shown, 2));   // GaugeFilter absorbs the dip: shown stays 0.6
        Assert.Empty(GaugeTrace.Flags([.. points.Skip(5).Select(p => p.Shown)], GaugeTrace.Jump));
    }
}
