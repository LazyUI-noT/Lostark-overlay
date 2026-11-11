using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Seconds left out of 버프 적용률 (dead-disabled.plan.md): the boss bar unread, or the skill bar tinted and 5 s after.</summary>
public class LeftOutTests
{
    readonly LeftOut _left = new();
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Every_second_the_boss_bar_is_unread_is_left_out_with_no_floor()
    {
        Assert.False(_left.Step(false, false, S(1)));
        Assert.True(_left.Step(true, false, S(2)));    // one second is enough
        Assert.False(_left.Step(false, false, S(3)));
    }

    [Fact]
    public void A_bar_not_read_yet_is_not_unread()
    {
        Assert.False(_left.Step(true, false, S(1)));
        _left.Step(false, false, S(2));
        Assert.True(_left.Step(true, false, S(3)));
        _left.Clear();                                  // a new record
        Assert.False(_left.Step(true, false, S(4)));
    }

    [Fact]
    public void A_tinted_skill_bar_is_left_out_and_five_seconds_after_it()
    {
        Assert.True(_left.Step(false, true, S(10)));
        Assert.True(_left.Step(false, false, S(15)));   // 5 s after the last tinted second
        Assert.False(_left.Step(false, false, S(15.1)));
    }

    [Fact]
    public void The_boss_figure_is_not_counted_until_it_has_settled_after_an_unread_bar()
    {
        _left.Step(false, false, S(1));
        Assert.False(_left.BossOut);
        _left.Step(true, false, S(2));
        Assert.True(_left.BossOut);
        Assert.False(_left.Step(false, false, S(3)));   // my damage counts again
        Assert.True(_left.BossOut);                     // the HP lost meanwhile comes in one jump, now or a read later
        _left.Step(false, false, S(4));
        Assert.True(_left.BossOut);
        _left.Step(false, false, S(5));
        Assert.False(_left.BossOut);
    }

    [Fact]
    public void The_second_counted_after_a_hidden_hud_is_left_out_too()
    {
        _left.Hide();                                   // nothing read yet: no unread bar
        Assert.False(_left.Step(false, false, S(1)));
        _left.Hide();                                   // the whole HUD gone: nothing is stepped
        Assert.True(_left.BossOut);
        Assert.True(_left.Step(false, false, S(9)));    // what is counted now was dealt across it
        Assert.False(_left.Step(false, false, S(10)));
    }

    [Fact]
    public void The_boss_figure_needs_no_settling_after_a_tint_alone()
    {
        _left.Step(false, false, S(1));
        _left.Step(false, true, S(2));
        Assert.True(_left.BossOut);
        _left.Step(false, false, S(8));
        Assert.False(_left.BossOut);
    }
}
