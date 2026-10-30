using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 줄 수 창's count and glow (BarAlert): reads settle before they show, an alert fires when the count goes past
/// it (target + offset), and glows for N seconds or N bars.</summary>
public class BarAlertTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    static readonly IReadOnlyList<int> At755 = BarAlert.Thresholds([750], 5);

    /// <summary>The state after reads (value, time) in order.</summary>
    static BarCountState Run(IReadOnlyList<int> thresholds, params (int? Read, double At)[] reads)
    {
        var s = BarCountState.None;
        foreach (var (r, t) in reads) s = BarAlert.Update(s, r, S(t), thresholds);
        return s;
    }

    [Fact]
    public void A_value_shows_after_two_agreeing_reads()
    {
        Assert.Null(Run(At755, (800, 0)).Shown);
        Assert.Equal(800, Run(At755, (800, 0), (800, 0.1)).Shown);
    }

    [Fact]
    public void A_one_frame_misread_neither_shows_nor_fires()
    {
        var s = Run(At755, (800, 0), (800, 0.1), (100, 0.2), (799, 0.3), (799, 0.4));
        Assert.Equal(799, s.Shown);
        Assert.False(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(0.5)));
    }

    [Fact]
    public void An_alert_fires_at_target_plus_offset_and_glows_for_the_seconds()
    {
        var s = Run(At755, (757, 0), (757, 0.1), (756, 1), (756, 1.1));
        Assert.False(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(1.2)));
        s = Run(At755, (757, 0), (757, 0.1), (755, 2), (755, 2.1));
        Assert.True(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(2.2)));
        Assert.True(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(22.0)));
        Assert.False(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(22.2)));
    }

    [Fact]
    public void In_bar_mode_it_glows_until_the_count_is_the_bars_past()
    {
        var s = Run(At755, (757, 0), (757, 0.1), (755, 1), (755, 1.1), (751, 2), (751, 2.1));
        Assert.True(BarAlert.Glowing(s, GlowUntil.Bars, 20, 5, S(100)));
        s = BarAlert.Update(BarAlert.Update(s, 750, S(3), At755), 750, S(3.1), At755);
        Assert.False(BarAlert.Glowing(s, GlowUntil.Bars, 20, 5, S(3.2)));
    }

    [Fact]
    public void A_count_that_skips_past_the_alert_still_fires()
    {
        var s = Run(At755, (760, 0), (760, 0.1), (740, 1), (740, 1.1));
        Assert.True(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(1.2)));
    }

    [Fact]
    public void A_count_going_back_up_re_arms_the_alert()
    {
        var s = Run(At755, (757, 0), (757, 0.1), (755, 1), (755, 1.1), (900, 5), (900, 5.1));   // a wipe and a new pull
        Assert.False(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(5.2)));
        s = BarAlert.Update(BarAlert.Update(s, 754, S(30), At755), 754, S(30.1), At755);
        Assert.True(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(30.2)));
    }

    [Fact]
    public void Short_gaps_keep_the_value_and_a_long_one_clears_it()
    {
        var s = Run(At755, (800, 0), (800, 0.1), (null, 2));
        Assert.Equal(800, s.Shown);
        Assert.Null(BarAlert.Update(s, null, S(3.2), At755).Shown);
    }

    [Fact]
    public void No_alerts_never_glow_and_the_first_value_never_fires()
    {
        Assert.False(BarAlert.Glowing(Run([], (800, 0), (800, 0.1), (100, 1), (100, 1.1)), GlowUntil.Seconds, 20, 5, S(1.2)));
        Assert.False(BarAlert.Glowing(Run(At755, (700, 0), (700, 0.1)), GlowUntil.Seconds, 20, 5, S(0.2)));   // joined below it
    }

    [Fact]
    public void Thresholds_add_the_offset_and_drop_what_falls_below_one()
    {
        Assert.Equal([755, 305], BarAlert.Thresholds([750, 300], 5));
        Assert.Equal([2], BarAlert.Thresholds([5, 2], -3));
    }

    [Fact]
    public void A_wipe_longer_than_the_lost_time_re_arms_the_alert()
    {
        // fired at 755, down to 750; the bar gone 10 s (a wipe), then a re-pull at 1000: no glow until 755 again.
        var s = Run(At755, (757, 0), (757, 0.1), (755, 1), (755, 1.1), (750, 2), (750, 2.1), (null, 12), (1000, 13), (1000, 13.1));
        Assert.False(BarAlert.Glowing(s, GlowUntil.Bars, 20, 5, S(13.2)));
        Assert.False(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(13.2)));
    }

    [Fact]
    public void A_shield_lifting_the_count_back_over_a_passed_alert_does_not_fire_it_again()
    {
        // 305 fired on the way down; at 296 a shield adds 10 bars (306), then the count falls through 305 again.
        var at305 = BarAlert.Thresholds([305], 0);
        var s = Run(at305, (310, 0), (310, 0.1), (305, 1), (305, 1.1), (296, 30), (296, 30.1), (306, 31), (306, 31.1));
        Assert.False(BarAlert.Glowing(s, GlowUntil.Bars, 20, 10, S(31.2)));   // over the alert again: no glow in bar mode either
        s = Run(at305, (310, 0), (310, 0.1), (305, 1), (305, 1.1), (296, 30), (296, 30.1), (306, 31), (306, 31.1), (305, 40), (305, 40.1), (300, 41), (300, 41.1));
        Assert.Equal(TimeSpan.FromSeconds(1.1), s.FiredTime);                   // still the first firing
        Assert.False(BarAlert.Glowing(s, GlowUntil.Seconds, 20, 5, S(41.2)));
    }
}
