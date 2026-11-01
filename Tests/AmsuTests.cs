using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The 암수 countdown (amsu.design.md, amsu.plan.md Task 4): reads into what the 암수 창 shows, and when to read.</summary>
public class AmsuTests
{
    const long Boss = 1_197_954_508_761;
    static TimeSpan T(double s) => TimeSpan.FromSeconds(s);
    static AmsuRow Row(params BombRead[] bombs) => new(bombs);
    static AmsuState Step(AmsuState s, double at, AmsuRow? row, long? boss = Boss, bool iconsOnly = false) => Amsu.Step(s, row, boss, T(at), iconsOnly);

    [Fact]
    public void A_read_starts_the_countdown_and_it_fades_while_unseen()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19)));
        Assert.Equal((Bomb.암수, 19.0, true), View(s, 0));

        s = Step(s, 4, null);
        Assert.Equal((Bomb.암수, 15.0, false), View(s, 4));
    }

    [Fact]
    public void A_rethrow_restarts_it()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 20)));
        s = Step(s, 12, Row(new BombRead(Bomb.암수, 20)));
        s = Step(s, 12.5, Row(new BombRead(Bomb.암수, 20)));   // the jump back to the full time, read twice: taken
        Assert.Equal(19.5, View(s, 12.5).Left, 1);
    }

    [Fact]
    public void A_lone_misread_doesnt_move_the_time()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19)));
        s = Step(s, 1, Row(new BombRead(Bomb.암수, 45)));   // "18" read as "45": over the bomb's length
        Assert.Equal(18, View(s, 1).Left, 1);
        s = Step(s, 1.5, Row(new BombRead(Bomb.암수, 7)));   // a lost "1" in "17"
        Assert.Equal(17.5, View(s, 1.5).Left, 1);
        s = Step(s, 2, Row(new BombRead(Bomb.암수, 17)));   // back on the countdown
        Assert.Equal(17, View(s, 2).Left, 1);
    }

    [Fact]
    public void The_one_with_more_time_left_is_shown()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19), new BombRead(Bomb.빛암, 22)));
        Assert.Equal(Bomb.빛암, View(s, 0).Shown);
        s = Step(s, 7, Row(new BombRead(Bomb.암수, 20), new BombRead(Bomb.빛암, 15)));
        s = Step(s, 7.5, Row(new BombRead(Bomb.암수, 20), new BombRead(Bomb.빛암, 14)));   // 암수 re-thrown (read twice)
        Assert.Equal(Bomb.암수, View(s, 7.5).Shown);
    }

    [Fact]
    public void Where_the_seconds_arent_read_a_bomb_is_up_while_its_icon_is_seen()
    {
        // 보스 디버프 크기 under 100% (replay 5: 90% misread the time and the kind): availability only, no countdown.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, null)), iconsOnly: true);
        foreach (double t in new[] { 10.0, 20, 30 }) s = Step(s, t, Row(new BombRead(Bomb.빛암, null)), iconsOnly: true);
        Assert.Equal(Bomb.빛암, View(s, 31).Shown);
        s = Step(s, 31, Row(), iconsOnly: true);
        Assert.Equal(Bomb.빛암, View(s, 31).Shown);   // a moment unseen (a popup): still up
        s = Step(s, 32, Row(), iconsOnly: true);
        Assert.Null(View(s, 32).Shown);   // 2 s unseen: gone
    }

    [Fact]
    public void Untimed_an_icon_back_after_a_gap_shows_at_once()
    {
        // Review (2026-11-01): the just-ended guard waits for a fresh "N초", which untimed reads never have.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, null)), iconsOnly: true);
        s = Step(s, 2, Row(), iconsOnly: true);   // hidden 2 s: ended at 1.5
        s = Step(s, 2.5, Row(new BombRead(Bomb.빛암, null)), iconsOnly: true);
        Assert.Equal(Bomb.빛암, View(s, 2.5).Shown);
    }

    [Theory]
    [InlineData(false, 0.5, false, true)]    // no settled line yet: search
    [InlineData(true, 0.5, false, false)]    // the settled line alone: cheap
    [InlineData(true, 2.0, false, true)]     // every 2 s the candidates too (a line that moved)
    [InlineData(true, 0.5, true, true)]      // a bomb up: at once, or a miss on the settled line ends it (user: 암수 blinked)
    public void The_candidate_lines_are_searched_when_needed(bool settled, double since, bool bombUp, bool search) =>
        Assert.Equal(search, AmsuSchedule.SearchLines(settled, T(since), bombUp));

    [Theory]
    [InlineData(80, false)]
    [InlineData(90, false)]
    [InlineData(100, true)]
    [InlineData(120, true)]
    public void The_time_is_read_from_100_percent(int bossSize, bool timed) => Assert.Equal(timed, Amsu.Timed(bossSize));

    [Fact]
    public void Where_the_seconds_read_an_icon_without_them_starts_nothing()
    {
        // Replay 2026-11-01: a popup over the row read as 암수 with no seconds; at 90%+ a real bomb shows its "N초".
        Assert.Null(View(Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, null))), 0).Shown);
    }

    [Fact]
    public void Out_of_view_it_keeps_counting_to_zero_then_hides()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19)));
        for (double t = 0.5; t < 19; t += 0.5) s = Step(s, t, Full());   // pushed out of a full row ("+N"): not seen
        Assert.Equal((Bomb.암수, 0.5, false), View(s, 18.5));
        s = Step(s, 19, Full());
        Assert.Null(View(s, 19).Shown);
    }

    static AmsuRow Full(params BombRead[] bombs) => new(bombs, Full: true);

    // The same icon read as the other kind (암수 and 빛암 share the art; 90% in the game, a fading icon at its end): a bomb keeps
    // its slot, and a read of the other kind there is that bomb seen (replay 8 and the engine-loop sweep, 2026-11-01).

    [Fact]
    public void A_read_of_the_other_kind_in_a_bombs_slot_is_that_bomb()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24, Slot: 0)));
        s = Step(s, 1, Row(new BombRead(Bomb.암수, 23, Slot: 0)));
        Assert.False(s.Up.ContainsKey(Bomb.암수));
        Assert.Equal((Bomb.빛암, 23.0, true), View(s, 1));
    }

    [Fact]
    public void Untimed_flips_of_the_kind_neither_end_the_bomb_nor_start_another()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, null, Slot: 1)), iconsOnly: true);
        for (double t = 0.5; t <= 4; t += 0.5)
            s = Step(s, t, Row(new BombRead(t % 1 == 0 ? Bomb.암수 : Bomb.빛암, null, Slot: 1)), iconsOnly: true);
        Assert.Equal(Bomb.빛암, View(s, 4).Shown);
        Assert.Single(s.Up);
    }

    [Fact]
    public void A_fading_icon_read_as_the_other_kind_starts_nothing()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19, Slot: 0)));
        s = Step(s, 19.5, Row(new BombRead(Bomb.빛암, 4, Slot: 0)));   // the 암수 just ended; its icon fades out
        Assert.Null(View(s, 19.5).Shown);
    }

    [Fact]
    public void Just_after_an_end_a_tail_read_of_the_other_kind_in_any_slot_starts_nothing()
    {
        // Engine-loop sweep, 21:9 clip: the 암수's last "0.8" read as 빛암 one slot over started a 25 s 빛암.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19, Slot: 3)));
        s = Step(s, 19.5, Row(new BombRead(Bomb.빛암, 0.8, Slot: 4)));
        Assert.Null(View(s, 19.5).Shown);
    }

    [Fact]
    public void Just_after_an_end_a_fresh_throw_of_the_other_kind_starts()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19, Slot: 3)));
        s = Step(s, 19.5, Row(new BombRead(Bomb.빛암, 24, Slot: 4)));
        Assert.Equal(Bomb.빛암, View(s, 19.5).Shown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_bomb_moving_into_the_slot_of_one_that_left_stays_itself(bool iconsOnly)
    {
        // Review (2026-11-01): 암수 in slot 0, 빛암 in slot 1; the 암수 leaves, the 빛암 moves to slot 0 and was read as the 암수.
        double? Sec(double s) => iconsOnly ? null : s;
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, Sec(19), Slot: 0), new BombRead(Bomb.빛암, Sec(24), Slot: 1)), iconsOnly: iconsOnly);
        s = Step(s, 1, Row(new BombRead(Bomb.빛암, Sec(23), Slot: 0)), iconsOnly: iconsOnly);   // within untimed's 1.5 s
        Assert.Equal(T(1), s.Up[Bomb.빛암].LastSeen);
        Assert.Equal(0, s.Up[Bomb.빛암].Slot);
        if (s.Up.TryGetValue(Bomb.암수, out var amsu)) Assert.Equal(T(0), amsu.LastSeen);
    }

    [Fact]
    public void Both_bombs_up_and_one_read_as_the_other_stays_each_its_own()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19, Slot: 0), new BombRead(Bomb.빛암, 24, Slot: 1)));
        s = Step(s, 1, Row(new BombRead(Bomb.빛암, 18, Slot: 0), new BombRead(Bomb.빛암, 23, Slot: 1)));   // slot 0 read as 빛암
        Assert.Equal(T(1), s.Up[Bomb.암수].LastSeen);
        Assert.Equal(T(1), s.Up[Bomb.빛암].LastSeen);
    }

    [Fact]
    public void A_fresh_throw_into_the_slot_of_a_bomb_that_just_ended_starts_at_once()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19, Slot: 0)));
        s = Step(s, 19.5, Row(new BombRead(Bomb.빛암, 24, Slot: 0)));
        Assert.Equal(Bomb.빛암, View(s, 19.5).Shown);
    }

    [Fact]
    public void Another_bomb_in_another_slot_starts()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24, Slot: 0)));
        s = Step(s, 1, Row(new BombRead(Bomb.빛암, 23, Slot: 0), new BombRead(Bomb.암수, 20, Slot: 1)));
        Assert.True(s.Up.ContainsKey(Bomb.암수));
    }

    [Fact]
    public void A_higher_read_short_of_a_fresh_throw_restarts_nothing()
    {
        // Replay 8: at 11 s left "11" read "17" twice, and 17 passed as a re-throw (16.8 s left).
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19)));
        s = Step(s, 8, Row(new BombRead(Bomb.암수, 17)));
        s = Step(s, 8.5, Row(new BombRead(Bomb.암수, 17)));
        Assert.Equal(10.5, View(s, 8.5).Left, 1);
    }

    [Fact]
    public void A_bomb_counts_on_while_the_whole_row_is_hidden()
    {
        // 120% sweep (2026-11-01): something over the boss row for 4 s; ended there, the bomb came back at its full time.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24)));
        for (double t = 0.5; t <= 4; t += 0.5) s = Step(s, t, new AmsuRow([], Seen: false));
        Assert.Equal((Bomb.빛암, 20.0, false), View(s, 4));
    }

    [Fact]
    public void A_bomb_gone_from_a_row_with_room_ends()
    {
        // User (2026-11-01): a bomb that had expired in the game kept counting, faded, as if pushed behind "+N". The row has
        // room, so nothing was pushed out: its icon gone is its end.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24)));
        s = Step(s, 1, Row());
        Assert.Equal(Bomb.빛암, View(s, 1).Shown);   // a moment unseen (a popup, a damage number): still up
        s = Step(s, 2, Row());
        Assert.Null(View(s, 2).Shown);
    }

    [Fact]
    public void A_lower_time_read_again_and_again_corrects_a_bomb_seen_from_its_throw()
    {
        // User (2026-11-01): the timer wasn't put right when read. A bomb first read at its full time took no lower read at
        // all (a guard against lost tens digits, which are now put back).
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24)));
        foreach (double t in new[] { 1.0, 1.5, 2, 2.5 }) s = Step(s, t, Row(new BombRead(Bomb.빛암, 15 - t)));
        Assert.Equal(12.5, View(s, 2.5).Left, 1);
    }

    [Fact]
    public void Another_boss_clears_it()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19)));
        s = Step(s, 1, Row(), boss: 25_807_352_833);
        s = Step(s, 1.5, Row(), boss: 25_807_352_833);   // read twice: another boss
        Assert.Null(View(s, 1.5).Shown);
    }

    [Fact]
    public void One_misread_of_the_boss_hp_keeps_the_bomb()
    {
        // Replay 2026-11-01: a popup over the bar misread its max HP once, and both bombs ended and restarted at full time.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24)));
        s = Step(s, 1, Row(new BombRead(Bomb.빛암, 23)), boss: 2_826_823);
        s = Step(s, 1.5, Row(new BombRead(Bomb.빛암, 22.5)));
        Assert.Equal(22.5, View(s, 1.5).Left, 1);
    }

    [Fact]
    public void Without_the_boss_bar_it_counts_on_and_the_same_boss_doesnt_bring_it_back()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19)));
        s = Step(s, 3, null, boss: null);   // a cutscene
        Assert.Equal(16, View(s, 3).Left, 1);
        s = Step(s, 30, Row(), boss: Boss);
        Assert.Null(View(s, 30).Shown);
    }

    // Replay (2026-11-01): reads that lose a tens digit ("19" as "9", "24" as "4") or misread a digit ("1" as "7") repeat, so
    // two agreeing reads aren't proof. A bomb's time only counts down or jumps back near its full length (a re-throw).

    [Fact]
    public void A_first_read_that_lost_its_tens_is_put_back_near_the_full_time()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 4)));   // "24" read as "4" (BuffTracker.FirstRead does the same)
        Assert.Equal(24, View(s, 0).Left, 1);
    }

    [Fact]
    public void A_late_first_read_keeps_its_own_low_time()
    {
        // Review (2026-11-01): a 빛암 first read at "3" (after a popup) became a trusted 23 and showed 20 s too long.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 3)));
        foreach (double t in new[] { 1.0, 2, 3 }) s = Step(s, t, Row(new BombRead(Bomb.빛암, 3 - t)));
        Assert.Null(View(s, 3.5).Shown);
    }

    [Fact]
    public void A_fresh_read_over_the_length_that_lost_its_tens_is_put_back()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 1)));   // "21" read as "1"
        Assert.Equal(21, View(s, 0).Left, 1);
    }

    [Fact]
    public void A_mis_kinded_read_of_the_other_bombs_time_without_its_tens_starts_nothing()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 23)));
        s = Step(s, 1, Row(new BombRead(Bomb.빛암, 22), new BombRead(Bomb.암수, 2)));   // the 빛암's "22" read as 암수 "2"
        Assert.False(s.Up.ContainsKey(Bomb.암수));
    }

    [Fact]
    public void A_fading_icons_tenths_without_their_point_restart_nothing()
    {
        // 90% sweep (2026-11-01): after the end the icon's "0.9초", "0.4초" read 9 and 4; restored, the 4 started a 24 s 빛암.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24)));
        s = Step(s, 24.5, Row(new BombRead(Bomb.빛암, 9)));
        s = Step(s, 25, Row(new BombRead(Bomb.빛암, 4)));
        Assert.Null(View(s, 25).Shown);
    }

    [Fact]
    public void Reads_that_lost_their_tens_are_put_back_from_the_countdown()
    {
        // Replay 4 (2026-11-01), 100%: the tens scores under BuffReader.MinTens, so 17, 16, 15, 14 read 7, 6, 5, 4, agreed as a
        // lower time and ended the 빛암 10 s early.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 18)));   // first seen mid-bomb: the full time, the 18 pending
        foreach (double t in new[] { 1.0, 2, 3, 4 }) s = Step(s, t, Row(new BombRead(Bomb.빛암, 8 - t)));
        Assert.Equal(14, View(s, 4).Left, 1);
    }

    [Fact]
    public void A_lower_time_is_taken_only_after_four_agreeing_reads()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 12)));   // seen mid-bomb (the app started late)
        foreach (double t in new[] { 0.5, 1.0 }) s = Step(s, t, Row(new BombRead(Bomb.암수, 12 - t)));
        Assert.Equal(19, View(s, 1).Left, 1);   // three reads: still the full time
        s = Step(s, 1.5, Row(new BombRead(Bomb.암수, 10.5)));
        Assert.Equal(10.5, View(s, 1.5).Left, 1);   // the fourth agrees: taken
    }

    [Fact]
    public void A_countdown_keeps_reads_that_lost_their_tens_again_and_again()
    {
        // Replay 3 (2026-11-01): "16" read as "6", again and again, ended a running 빛암 early. The game shows whole seconds
        // over 1 s: 16, 15, 15, 14, 14 read 6, 5, 5, 4, 4.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24)));
        foreach (var (t, shown) in new[] { (8.0, 6), (8.5, 5), (9.0, 5), (9.5, 4), (10.0, 4) }) s = Step(s, t, Row(new BombRead(Bomb.빛암, shown)));
        Assert.Equal(14, View(s, 10).Left, 1);
    }

    [Fact]
    public void A_new_bomb_reading_the_other_kinds_time_is_that_bomb_misread()
    {
        // Replay 3: the 빛암 icon read as 암수 at 90% for a moment; its seconds were the 빛암's.
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 23)));
        s = Step(s, 2, Row(new BombRead(Bomb.암수, 21)));
        Assert.False(s.Up.ContainsKey(Bomb.암수));
    }

    [Fact]
    public void A_jump_up_that_is_not_a_fresh_throw_is_ignored()
    {
        var s = Step(AmsuState.None, 0, Row(new BombRead(Bomb.암수, 19)));
        foreach (double t in new[] { 18.0, 18.5, 19.0 }) s = Step(s, t, Row(new BombRead(Bomb.암수, 7)));   // "1" read as "7", again and again
        Assert.Equal(0, View(s, 19).Left, 1);   // still ends on time
    }

    [Fact]
    public void Edges_name_a_bomb_seen_rethrown_and_gone()
    {
        var a = Step(AmsuState.None, 0, Row(new BombRead(Bomb.빛암, 24)));
        Assert.Equal([new AmsuEdge(Bomb.빛암, AmsuChange.시작, 24)], Amsu.Edges(AmsuState.None, a, T(0)));

        var b = Step(a, 1, Row(new BombRead(Bomb.빛암, 23)));
        Assert.Empty(Amsu.Edges(a, b, T(1)));   // on the countdown: nothing new

        var c = Step(Step(b, 10, Row(new BombRead(Bomb.빛암, 25))), 10.5, Row(new BombRead(Bomb.빛암, 25)));
        Assert.Equal([new AmsuEdge(Bomb.빛암, AmsuChange.재시작, 24.5)], Amsu.Edges(b, c, T(10.5)));

        var d = Step(c, 40, Row());
        Assert.Equal([new AmsuEdge(Bomb.빛암, AmsuChange.끝, 0)], Amsu.Edges(c, d, T(40)));
    }

    [Theory]
    [InlineData(0.5, 0.3, 12.0, true, false)]    // icons every 0.5 s
    [InlineData(0.4, 0.3, 12.0, false, false)]
    [InlineData(0.4, 0.25, 6.5, false, true)]   // under 7 s: the timer every 0.25 s between icon reads
    [InlineData(0.4, 0.2, 6.5, false, false)]
    [InlineData(0.4, 0.3, null, false, false)]  // nothing up: no timer read
    public void Reads_icons_every_half_second_and_the_timer_faster_near_the_end(double sinceIcons, double sinceTimer, double? left, bool icons, bool timer) =>
        Assert.Equal((icons, timer), AmsuSchedule.Due(left, T(sinceIcons), T(sinceTimer)));

    static (Bomb? Shown, double Left, bool Seen) View(AmsuState s, double at) => Amsu.View(s, T(at));
}
