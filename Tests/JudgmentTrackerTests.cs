using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>단심 (user, 2026-10-13): 심판 lasts 6 s and comes back 30 s after it started. Seen as its icon in the buff row
/// or its text over the character; the moment it is first seen sets the cycle.</summary>
public class JudgmentTrackerTests
{
    static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);
    static TimeSpan At(double s) => T0 + TimeSpan.FromSeconds(s);
    static readonly JudgmentRead None = new(false, false), Icon = new(true, false), Text = new(false, true);

    static JudgmentState Run(params (double At, JudgmentRead Read)[] reads)
    {
        var s = JudgmentState.Ready;
        foreach (var (t, r) in reads) s = JudgmentTracker.Update(s, r, At(t), hudVisible: true);
        return s;
    }

    static void Near(double expected, double actual) => Assert.Equal(expected, actual, 9);

    [Fact]
    public void Ready_until_a_trigger_is_seen()
    {
        Assert.Equal(JudgmentPhase.Ready, Run((0, None), (1, None)).Phase(At(1)));
    }

    [Theory]
    [InlineData(0, JudgmentPhase.Active)]
    [InlineData(5.9, JudgmentPhase.Active)]
    [InlineData(6, JudgmentPhase.Cooldown)]
    [InlineData(29.9, JudgmentPhase.Cooldown)]
    [InlineData(30, JudgmentPhase.Ready)]
    public void Six_seconds_on_then_cooldown_to_thirty(double after, JudgmentPhase phase)
    {
        Assert.Equal(phase, Run((0, Text), (0.1, Text)).Phase(At(after)));
    }

    [Theory]
    [InlineData(0, 6, 24)]
    [InlineData(3, 3, 24)]
    [InlineData(16, 0, 14)]
    [InlineData(31, 0, 0)]
    public void The_bar_drains_over_thirty_seconds_with_the_active_part_first(double after, double active, double cooldown)
    {
        var (a, c) = Run((0, Text), (0.1, Text)).Fill(At(after));
        Near(active / 30, a);
        Near(cooldown / 30, c);
    }

    [Fact]
    public void An_unknown_cycle_shows_empty() => Assert.Equal((0, 0), JudgmentState.Ready.Fill(At(0)));

    [Theory]
    [InlineData(0, 6)]
    [InlineData(0.5, 5)]     // whole seconds left, rounded down like the buff bars
    [InlineData(5.9, 0)]
    [InlineData(6, 24)]      // then seconds to ready
    [InlineData(29.5, 0)]
    public void The_label_counts_심판_then_the_wait(double after, int label)
    {
        Assert.Equal(label, Run((0, Text), (0.1, Text)).LabelSeconds(At(after)));
    }

    [Fact]
    public void No_label_while_ready() => Assert.Null(JudgmentState.Ready.LabelSeconds(At(0)));

    [Fact]
    public void The_icon_staying_on_or_blinking_under_glare_is_one_trigger()
    {
        var s = Run((0, Text), (0.1, Text), (0.3, new(true, true)), (2, Icon), (5.9, None), (6.2, Icon), (6.4, None));
        Assert.Equal(At(0), s.TriggeredAt);
    }

    [Fact]
    public void A_new_trigger_during_the_cooldown_restarts_the_cycle()
    {
        var s = Run((0, Text), (0.1, Text), (1, None), (15, Text), (15.1, Text));
        Assert.Equal(At(15), s.TriggeredAt);
    }

    [Fact]
    public void Text_still_showing_long_after_isnt_a_new_trigger()
    {
        var s = Run((0, Text), (0.1, Text), (3, Text), (8, Text), (12, Text));
        Assert.Equal(At(0), s.TriggeredAt);
    }

    [Fact]
    public void One_poll_of_text_alone_is_not_enough() =>
        Assert.Equal(JudgmentPhase.Ready, Run((0, Text), (0.1, None), (0.2, Text), (0.3, None)).Phase(At(0.3)));

    [Fact]
    public void A_six_seconds_timer_never_dates_the_start_in_the_future() =>
        Assert.Equal(At(10), Run((10, Timer(6))).TriggeredAt);

    [Fact]
    public void A_poll_just_past_the_watch_window_does_not_restart_it() =>
        Assert.Equal(At(0), Run((0, Icon), (6.9, None), (7.05, Icon)).TriggeredAt);

    [Fact]
    public void An_icon_alone_starts_the_cycle_too() => Assert.Equal(At(10), Run((10, None), (10, Icon)).TriggeredAt);

    // The icon's timer (user, 2026-10-13): "N초" is N to N+1 seconds left, the moment it ticks down exactly N+1.
    static JudgmentRead Timer(double seconds) => new(true, false, seconds);

    [Fact]
    public void A_tick_of_the_icons_timer_dates_the_trigger_exactly()
    {
        var s = Run((9.2, Timer(2)), (10, Timer(1)));
        Assert.Equal(At(10) - TimeSpan.FromSeconds(4), s.TriggeredAt);   // 2 s left at 10 s: 심판 began at 6 s
    }

    [Fact]
    public void An_icon_first_seen_mid_buff_dates_the_trigger_from_its_timer()
    {
        var s = Run((0, None), (10, Timer(3)));
        Assert.Equal(At(10) - TimeSpan.FromSeconds(2.5), s.TriggeredAt);   // 3.5 s left, the middle of "3초"
    }

    [Fact]
    public void A_timer_that_agrees_with_the_cycle_leaves_it()
    {
        var s = Run((0, Text), (0.1, Text), (2.3, Timer(3)), (2.9, Timer(3)));
        Assert.Equal(At(0), s.TriggeredAt);
    }

    [Fact]
    public void A_timer_that_disagrees_moves_the_cycle()
    {
        var s = Run((0, Text), (0.1, Text), (1, None), (3, Timer(1)));   // the text was early or false: 1–2 s left at 3 s
        Assert.Equal(At(3) - TimeSpan.FromSeconds(4.5), s.TriggeredAt);
    }

    [Fact]
    public void Its_last_second_is_read_to_the_tenth()
    {
        var s = Run((0, None), (10, Timer(0.4)));
        Assert.Equal(At(10) - TimeSpan.FromSeconds(5.6), s.TriggeredAt);
    }

    // Read only while it can show (user, 2026-10-13): ready, or 심판 on and a second past, for its icon's timer.
    [Theory]
    [InlineData(null, true)]
    [InlineData(0.0, true)]
    [InlineData(6.9, true)]
    [InlineData(7.1, false)]
    [InlineData(29.9, false)]
    [InlineData(30.0, true)]
    public void It_is_watched_only_while_ready_or_on(double? after, bool watched)
    {
        var s = after is null ? JudgmentState.Ready : Run((0, Text), (0.1, Text));
        Assert.Equal(watched, s.Watching(At(after ?? 0)));
    }

    [Fact]
    public void After_an_unwatched_cooldown_the_next_text_is_a_new_trigger()
    {
        var s = Run((0, Text), (0.1, Text), (1, Text));   // last read with the text still showing, then nothing read for the cooldown
        s = JudgmentTracker.Update(s, Text, At(31), hudVisible: true);
        s = JudgmentTracker.Update(s, Text, At(31.1), hudVisible: true);
        Assert.Equal(At(31), s.TriggeredAt);
    }

    [Fact]
    public void A_hidden_HUD_keeps_the_clock_and_the_same_icon_after_it_isnt_a_new_trigger()
    {
        var s = Run((0, Icon));
        s = JudgmentTracker.Update(s, None, At(2), hudVisible: false);
        s = JudgmentTracker.Update(s, Icon, At(3), hudVisible: true);
        Assert.Equal(At(0), s.TriggeredAt);
        Assert.Equal(JudgmentPhase.Cooldown, s.Phase(At(10)));
    }
}
