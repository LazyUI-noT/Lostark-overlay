using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public sealed class DelayStatsTests
{
    [Fact]
    public void Empty_gives_zeros() => Assert.Equal((0.0, 0.0, 0.0), DelayStats.Of([]));

    [Fact]
    public void One_sample_is_every_figure() => Assert.Equal((4.0, 4.0, 4.0), DelayStats.Of([4]));

    [Fact]
    public void Nearest_rank_over_unsorted_samples()
    {
        double[] ms = [.. Enumerable.Range(1, 100).Select(i => (double)(101 - i))];   // 100 … 1

        Assert.Equal((50.0, 95.0, 100.0), DelayStats.Of(ms));
    }

    [Fact]
    public void Even_count_median_is_the_lower_middle() => Assert.Equal((1.0, 2.0, 2.0), DelayStats.Of([2, 1]));

    [Fact]
    public void Negative_samples_pass_through() => Assert.Equal((-1.0, 5.0, 5.0), DelayStats.Of([5, -3, -1]));

    [Fact]
    public void Text_is_median_p95_max() => Assert.Equal("2.0/9.0/9.0", DelayStats.Text([9, 1, 2]));
}
