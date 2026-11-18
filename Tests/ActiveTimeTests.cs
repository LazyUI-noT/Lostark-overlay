using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>리소스 검사's active time (user, 2026-11-18): each run is active from its start to the grace after its end.</summary>
public class ActiveTimeTests
{
    const double Grace = 3;

    [Fact]
    public void Nothing_ran_is_zero()
    {
        Assert.Equal(0, new ActiveTime(Grace).Seconds);
    }

    [Fact]
    public void Runs_closer_than_the_grace_count_the_whole_stretch_and_the_grace_after_the_last()
    {
        var t = new ActiveTime(Grace);
        foreach (var at in new[] { 10.0, 11, 12, 13 }) t.Hit(at, at);
        Assert.Equal(3 + Grace, t.Seconds, 6);
    }

    [Fact]
    public void A_gap_longer_than_the_grace_counts_only_the_grace()
    {
        var t = new ActiveTime(Grace);
        t.Hit(10, 10);
        t.Hit(60, 60);   // town in between
        Assert.Equal(2 * Grace, t.Seconds, 6);
    }

    [Fact]
    public void A_run_that_ended_a_moment_before_the_last_is_already_covered()
    {
        var t = new ActiveTime(Grace);
        t.Hit(10, 10);
        t.Hit(9.9, 9.9);   // another thread's part of the same box
        Assert.Equal(Grace, t.Seconds, 6);
    }

    [Fact]
    public void A_long_run_counts_its_own_length()
    {
        var t = new ActiveTime(Grace);
        t.Hit(2, 10);   // a part that ran 8 s
        t.Hit(12, 20);  // and again, starting inside the first one's grace
        Assert.Equal(20 + Grace - 2, t.Seconds, 6);
    }

    [Fact]
    public void A_run_that_only_just_ended_counts_its_whole_grace()
    {
        var t = new ActiveTime(Grace);
        t.Hit(9.96, 10);   // 40 ms, read right after: not divided by a sliver
        Assert.Equal(0.04 + Grace, t.Seconds, 6);
    }
}
