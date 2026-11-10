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

    // The bar (user, 2026-10-13): while 심판 is on, the whole bar is its 6 s; then the whole bar is the 24 s cooldown.
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(3, 0.5, 0)]
    [InlineData(6, 0, 1)]
    [InlineData(18, 0, 0.5)]
    [InlineData(31, 0, 0)]
    public void The_bar_is_the_6_seconds_then_the_24_second_cooldown(double after, double active, double cooldown)
    {
        var (a, c) = Run((0, Text), (0.1, Text)).Fill(At(after));
        Near(active, a);
        Near(cooldown, c);
    }

    [Fact]
    public void An_unknown_cycle_shows_empty() => Assert.Equal((0, 0), JudgmentState.Ready.Fill(At(0)));

    [Theory]
    [InlineData(0, 5)]       // "5" from 5.9 s: a whole 6.0 never shows
    [InlineData(0.5, 5)]     // whole seconds left, rounded down like the buff bars
    [InlineData(5.45, 0.5)]  // tenths under a second (user, 2026-11-10): 0.55 left shows 0.5
    [InlineData(5.9, 0)]     // 0.1 left shows 0.0
    [InlineData(6, 23)]      // then seconds to ready
    [InlineData(29.5, 0.4)]   // an exact 0.5 shows 0.4
    public void The_label_counts_심판_then_the_wait(double after, double label)
    {
        Assert.Equal(label, Run((0, Text), (0.1, Text)).LabelSeconds(At(after)));
    }

    [Theory]
    [InlineData(2.0, "2")]
    [InlineData(19.0, "19")]
    [InlineData(0.4, "0.4")]
    [InlineData(0.0, "0.0")]
    public void A_label_is_whole_from_1_s_and_tenths_under(double seconds, string text) =>
        Assert.Equal(text, JudgmentState.LabelText(seconds));

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

    // 아이콘 (user, 2026-11-10): 심판's icon with its seconds while it is on, grey with the seconds to ready through the
    // cooldown, a white flash for 0.4 s as it is ready, then nothing until 단죄 runs (user, 2026-11-10).
    [Theory]
    [InlineData(0.0, false, false, false, 5.0)]      // "5" from 5.9 s: 6.0 never shows
    [InlineData(2.5, false, false, false, 3.0)]
    [InlineData(10.5, false, true, false, 19.0)]     // cooldown: grey, the seconds to ready
    [InlineData(30.1, true, false, true, null)]    // ready: under the flash
    public void The_icon_counts_down_greys_then_flashes_when_ready(double after, bool conviction, bool dim, bool flash, double? seconds)
    {
        var i = Run((0, Text), (0.1, Text)).Icon(At(after))!.Value;
        Assert.Equal((conviction, dim, flash, seconds), (i.Conviction, i.Dim, i.Flash, i.Seconds));
    }

    [Fact]
    public void The_icon_is_gone_while_ready_with_no_단죄() => Assert.Null(Run((0, Text), (0.1, Text)).Icon(At(31.5)));

    [Theory]
    [InlineData(0.0, 3.0, 2.0)]    // "2" from the start: 3 never shows
    [InlineData(0.1, 2.9, 2.0)]
    [InlineData(1.0, 2.0, 1.0)]
    [InlineData(2.5, 0.5, 0.4)]      // tenths under a second; an exact 0.5 shows 0.4
    [InlineData(2.95, 0.05, 0.0)]
    [InlineData(3.0, null, null)]    // its 3 s are over
    public void 단죄_runs_3_s_while_ready(double after, double? left, double? label)
    {
        var s = JudgmentState.Ready with { ConvictionAt = At(0) };
        Assert.Equal(left, s.ConvictionLeft(At(after)) is { } l ? Math.Round(l, 9) : null);
        Assert.Equal(label, s.LabelSeconds(At(after)));
        Assert.Equal(left is null ? null : new JudgmentIcon(true, false, false, label), s.Icon(At(after)));
    }

    [Fact]
    public void 단죄_waits_under_a_running_심판()
    {
        var s = Run((0, Text), (0.1, Text)) with { ConvictionAt = At(2) };
        Assert.Null(s.ConvictionLeft(At(2.5)));
        Assert.Equal(3, s.LabelSeconds(At(2.5)));   // 심판's seconds
    }

    [Fact]
    public void 단죄_shows_before_any_심판_is_seen() =>
        Assert.Equal(new JudgmentIcon(true, false, false, 2), Run((0, Conv())).Icon(At(0.1)));

    [Fact]
    public void The_ready_flash_survives_the_cycle_being_reset_by_a_read()
    {
        var s = Run((0, Text), (0.1, Text));
        s = JudgmentTracker.Update(s, None, At(30.05), hudVisible: true);   // the first poll after the cycle: ready again
        Assert.Equal(JudgmentPhase.Ready, s.Phase(At(30.1)));
        Assert.True(s.Icon(At(30.1))?.Flash);
    }

    [Fact]
    public void The_next_심판_replaces_the_last_cycles_flash()
    {
        var s = Run((0, Text), (0.1, Text));
        s = JudgmentTracker.Update(s, None, At(30.05), hudVisible: true);
        s = JudgmentTracker.Update(s, Icon, At(30.2), hudVisible: true);   // 심판 again, inside the old flash
        Assert.Equal(new JudgmentIcon(false, false, false, 5), s.Icon(At(30.2)));
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

    // 단죄 (user, 2026-11-10): its icon in the buff row, "2초" … "0" under it (3 s, the game rounding down); a new sighting or
    // its timer dates it, a retrigger (the timer jumping back up) moves it.
    static JudgmentRead Conv(double? seconds = null) => new(false, false, Conviction: true, ConvictionSeconds: seconds);

    [Fact]
    public void A_new_단죄_sighting_starts_its_3_s() =>
        Assert.Equal(3, Run((0, None), (1, Conv())).ConvictionLeft(At(1)));

    [Fact]
    public void 단죄s_first_timer_read_starts_it_at_the_top_of_its_window() =>
        Assert.Equal(3, Run((1, Conv(2))).ConvictionLeft(At(1))!.Value, 9);

    [Fact]
    public void A_tick_of_단죄s_timer_dates_it_exactly() =>
        Assert.Equal(2, Run((1, Conv(2)), (1.4, Conv(1)), (1.6, Conv(1))).ConvictionLeft(At(1.4))!.Value, 9);

    [Fact]
    public void A_retriggered_단죄_starts_its_3_s_again_on_a_second_read() =>
        Assert.Equal(3, Run((0, Conv(2)), (2.3, Conv(0.7)), (2.5, Conv(2)), (2.6, Conv(2))).ConvictionLeft(At(2.6))!.Value, 9);

    [Fact]
    public void One_wild_read_is_a_blip()
    {
        var s = Run((0, Conv(2)), (0.2, Conv(0.5)));   // 1.8 s off the held end: waits
        Assert.Equal(2.8, s.ConvictionLeft(At(0.2))!.Value, 9);
        s = JudgmentTracker.Update(s, Conv(2), At(0.25), hudVisible: true);   // the next read agrees with the held end
        Assert.Equal((2.75, null), (Math.Round(s.ConvictionLeft(At(0.25))!.Value, 9), s.ConvictionPending));
    }

    [Fact]
    public void A_read_past_the_held_end_keeps_it_on() =>
        Assert.NotNull(Run((0, Conv(2)), (3.2, Conv(0.1))).ConvictionLeft(At(3.2)));

    [Fact]
    public void A_hidden_HUD_drops_a_pending_jump() =>
        Assert.Null(JudgmentTracker.Update(Run((0, Conv(2)), (0.2, Conv(0.5))), None, At(0.3), hudVisible: false).ConvictionPending);

    [Fact]
    public void Its_tenths_date_the_last_second() =>
        Assert.Equal(0.4, Run((0, Conv(2)), (2.2, Conv(0.8)), (2.6, Conv(0.4))).ConvictionLeft(At(2.6))!.Value, 9);

    // Under a second, as the buff bars (user, 2026-11-10): glare turns "0.4" into "0.9" for a frame.
    [Fact]
    public void A_tenths_read_above_the_held_end_waits_for_a_second_read()
    {
        var s = Run((0, Conv(2)), (2.2, Conv(0.8)), (2.5, Conv(0.5)), (2.6, Conv(0.9)));
        Assert.Equal(0.4, s.ConvictionLeft(At(2.6))!.Value, 9);
        s = JudgmentTracker.Update(s, Conv(0.85), At(2.65), hudVisible: true);   // a second read agrees within two tenths
        Assert.Equal(0.85, s.ConvictionLeft(At(2.65))!.Value, 9);
    }

    [Fact]
    public void A_second_tenths_read_that_disagrees_keeps_waiting()
    {
        var s = Run((0, Conv(2)), (2.2, Conv(0.8)), (2.5, Conv(0.5)), (2.6, Conv(0.9)), (2.65, Conv(0.6)));
        Assert.Equal((0.35, true), (Math.Round(s.ConvictionLeft(At(2.65))!.Value, 9), s.ConvictionPending is not null));
    }

    [Fact]
    public void A_tenths_read_past_the_held_end_is_taken_at_once() =>
        Assert.Equal(0.5, Run((0, Conv(2)), (3.1, Conv(0.5))).ConvictionLeft(At(3.1))!.Value, 9);

    [Theory]
    [InlineData(6, 2.2, 0.6)]   // lost its point
    [InlineData(4, 2.2, 0.4)]
    [InlineData(3, 0.1, 3)]   // a whole read 단죄 can show: kept, 3.0 s left (3 s again from the read, not 0.3)
    public void A_whole_read_past_3_is_tenths_that_lost_their_point(double shown, double at, double left) =>
        Assert.Equal(left, Run((0, Conv(2)), (at, Conv(shown))).ConvictionLeft(At(at))!.Value, 9);

    [Fact]
    public void A_first_read_that_lost_its_point_starts_it_there() =>
        Assert.Equal(0.7, Run((1, Conv(7))).ConvictionLeft(At(1))!.Value, 9);

    [Fact]
    public void 단죄_still_seen_past_its_3_s_without_a_timer_doesnt_restart() =>
        Assert.Null(Run((0, Conv()), (3.5, Conv())).ConvictionLeft(At(3.5)));

    // The bug (user, 2026-11-10): the number flicked back up. Triggered at 0, polled every 50 ms from 30 ms, each poll
    // reading the timer as the game shows it then (whole seconds rounded down, tenths under one), drawn every 10 ms: the
    // number only goes down, and is never above the game's of a poll ago (a tick is seen at the next poll).
    static double Shown(double left) => left >= 1 ? Math.Floor(left) : Math.Floor(Math.Round(left * 10, 6)) / 10;

    [Fact]
    public void 단죄s_number_never_goes_up()
    {
        var s = JudgmentState.Ready;
        double? last = null;
        for (int ms = 0; ms < 3400; ms += 10)
        {
            double t = ms / 1000.0;
            if (ms % 50 == 30 && ms < 3000) s = JudgmentTracker.Update(s, Conv(Shown(3 - t)), At(t), hudVisible: true);
            if (s.LabelSeconds(At(t)) is not { } n) continue;
            Assert.True(last is null || n <= last, $"{last} → {n} at {t:0.00}");
            Assert.True(n <= Shown(3 - (t - 0.05)) + 1e-9, $"{n} above the game's at {t:0.00}");
            last = n;
        }
    }

    [Fact]
    public void 심판_ends_단죄_at_once() =>
        Assert.Null(Run((0, Conv(2)), (0.1, Icon)).ConvictionLeft(At(0.2)));

    // Review (2026-11-10): a 단죄 read while 심판 runs (a misread, or a box beside it) would restart its 3 s every poll.
    [Fact]
    public void 단죄_seen_while_심판_runs_is_ignored() =>
        Assert.Null(Run((0, Icon), (1, Conv()), (2, Conv(2))).ConvictionAt);
}
