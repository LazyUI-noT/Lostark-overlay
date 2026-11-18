using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class BossHpFightTests
{
    const long Max = 1_142_325_546_571;
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void A_spike_is_dropped_and_the_last_good_read_kept()
    {
        var fight = new BossHpFight();
        Assert.Equal((96_198_309_725L, Max, 0L), fight.Step((96_198_309_725, Max, 0), false, S(0)));
        Assert.Equal((96_198_309_725L, Max, 0L), fight.Step((96_198_309_725, 1_112_325_546_571, 0), false, S(0.1)));   // max misread
        Assert.Equal((96_198_309_725L, Max, 0L), fight.Step((99_198_309_725, Max, 0), false, S(0.2)));                 // HP up
        Assert.Equal((96_000_000_000L, Max, 0L), fight.Step((96_000_000_000, Max, 0), false, S(0.3)));
    }

    [Fact]
    public void The_last_good_read_is_kept_for_two_seconds_then_none()
    {
        var fight = new BossHpFight();
        fight.Step((96_198_309_725, Max, 0), false, S(0));
        Assert.NotNull(fight.Step((1, 2, 0), false, S(1)));   // dropped spikes, the last kept
        Assert.NotNull(fight.Step((5, 9, 0), false, S(2)));
        Assert.Null(fight.Step((7, 9, 0), false, S(2.1)));
    }

    [Fact]
    public void A_failed_read_keeps_the_last_only_while_the_bar_is_up()
    {
        var fight = new BossHpFight();
        fight.Step((96_198_309_725, Max, 0), false, S(0));
        Assert.Equal((96_198_309_725L, Max, 0L), fight.Step(null, barUp: true, S(0.5)));
        Assert.Null(fight.Step(null, barUp: false, S(0.6)));   // no bar: as before
    }

    [Fact]
    public void A_new_boss_counts_once_the_next_read_agrees()
    {
        var fight = new BossHpFight();
        fight.Step((96_198_309_725, Max, 0), false, S(0));
        Assert.Equal((96_198_309_725L, Max, 0L), fight.Step((5_000_000, 9_000_000, 0), false, S(0.1)));
        Assert.Equal((5_000_000L, 9_000_000L, 0L), fight.Step((5_000_000, 9_000_000, 0), false, S(0.2)));
    }

    [Fact]
    public void A_small_boss_falling_fast_counts_from_its_second_read()
    {
        var fight = new BossHpFight();
        fight.Step((9_000_000, 9_000_000, 0), false, S(0));
        Assert.Equal((9_000_000L, 9_000_000L, 0L), fight.Step((7_000_000, 9_000_000, 0), false, S(0.5)));   // 22% in 0.5 s
        Assert.Equal((5_000_000L, 9_000_000L, 0L), fight.Step((5_000_000, 9_000_000, 0), false, S(1)));     // falling after it
    }

    [Fact]
    public void A_shield_falls_at_once_and_a_new_one_counts_once_the_next_read_agrees()
    {
        var fight = new BossHpFight();
        fight.Step((96_198_309_725, Max, 321_823_151_269), false, S(0));
        Assert.Equal((96_198_309_725L, Max, 0L), fight.Step((96_198_309_725, Max, 0), false, S(0.1)));   // broken
        Assert.Equal((96_198_309_725L, Max, 0L), fight.Step((96_198_309_725, Max, 50_000_000_000), false, S(0.2)));
        Assert.Equal((96_198_309_725L, Max, 50_000_000_000L), fight.Step((96_198_309_725, Max, 50_000_000_000), false, S(0.3)));
    }

    [Fact]
    public void A_read_a_little_under_the_hp_doesnt_hold_back_the_right_one()
    {
        var fight = new BossHpFight();
        fight.Step((96_198_309_725, Max, 0), false, S(0));
        fight.Step((96_198_309_723, Max, 0), false, S(0.1));
        Assert.Equal((96_198_309_725L, Max, 0L), fight.Step((96_198_309_725, Max, 0), false, S(0.2)));
    }

    [Fact]
    public void A_spike_above_the_hp_never_starts_a_chain()
    {
        var fight = new BossHpFight();
        fight.Step((96_000_000_000, Max, 0), false, S(0));
        fight.Step((99_000_000_000, Max, 0), false, S(0.1));
        Assert.Equal((96_000_000_000L, Max, 0L), fight.Step((97_500_000_000, Max, 0), false, S(0.2)));   // under the spike, over the HP (neither within 0.1%)
        Assert.True(fight.Held);
    }

    [Fact]
    public void Held_says_whether_the_read_is_new()
    {
        var fight = new BossHpFight();
        fight.Step((96_198_309_725, Max, 0), false, S(0));
        Assert.False(fight.Held);
        fight.Step(null, barUp: true, S(0.1));
        Assert.True(fight.Held);
        fight.Step(null, barUp: false, S(0.2));
        Assert.False(fight.Held);
    }

    [Fact]
    public void After_a_reset_any_read_starts_the_fight()
    {
        var fight = new BossHpFight();
        fight.Step((96_198_309_725, Max, 0), false, S(0));
        fight.Reset();
        Assert.Null(fight.Step(null, barUp: true, S(0.1)));
        Assert.Equal((5L, 9L, 0L), fight.Step((5, 9, 0), false, S(0.2)));
    }
}
