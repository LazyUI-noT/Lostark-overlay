using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The recorder (spec § D Fight clock): events on the 전분 clock, held until the timer moves past them, dropped while
/// it is stopped; samples once a second; when a fight ends.</summary>
public class FightRecorderTests
{
    static readonly DateTime Wall = new(2026, 10, 30, 21, 0, 0);
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    FightClockState _clock = FightClockState.None;
    readonly FightRecorder _r = new() { Me = "나", MyClass = 3, MySubclass = "고독한기사" };

    FightRecord? Tick(int? timer, double at, params MeterSample[] samples)
    {
        _clock = FightClock.Update(_clock, timer, S(at));
        return _r.Step(MeterStatus.None with { Clock = _clock }, samples, [new RecordMember("나", 3, MeterRole.딜러, 1)], 1_000_000_000, "act3-g3", S(at), Wall);
    }

    static Snapshot Snap(SlotPhase phase, double at, bool aden = false, BuffKind? on = null)
    {
        var s = Snapshot.Empty(S(at));
        on ??= aden ? BuffKind.아덴피증 : null;
        return s with
        {
            Slots = s.Slots.SetItem((int)SlotId.일반3, new SlotState(SlotId.일반3, phase, null, null, false, S(at))),
            Buffs = [.. Enum.GetValues<BuffKind>().Select(k => k == on ? new BuffState(k, true, null, null, false, S(at)) : BuffState.Absent(k, S(at)))],
        };
    }

    void See(string name, Snapshot snap, double at)
    {
        _r.ObserveMine(snap, S(at));
    }

    void Cast(string name, double at)
    {
        See(name, Snap(SlotPhase.Ready, at), at);
        See(name, Snap(SlotPhase.Cooldown, at + 0.05), at + 0.05);
    }

    static MeterSample Me(long damage) => new(0, "나", 3, MeterRole.딜러, 1, damage, damage / 10, null, true, 1, 0);

    FightRecord? TickBar(int timer, double at, double hpAt, long taken, double tintAt = 0)
    {
        _clock = FightClock.Update(_clock, timer, S(at));
        return _r.Step(MeterStatus.None with { Clock = _clock, HpAt = S(hpAt), TintAt = S(tintAt) }, [Me(taken)], [new RecordMember("나", 3, MeterRole.딜러, 1)], taken, "act3-g3", S(at), Wall);
    }

    [Fact]
    public void The_seconds_the_boss_bar_is_unread_are_recorded_as_left_out()
    {
        for (int t = 0; t <= 3; t++) TickBar(t, t, t, t * 100);
        for (int t = 4; t <= 7; t++) TickBar(t, t, 3, 300);   // unread from 5 on (read at 3, fresh for 1.5 s)
        TickBar(8, 8, 8, 900);                                 // back: the HP lost meanwhile comes in one jump, left out too
        Assert.Equal([new RecordSpan(4, 8)], _r.Finish()!.Assumed);
    }

    [Fact]
    public void The_boss_figure_settles_for_two_seconds_after_an_unread_bar_then_counts_again()
    {
        for (int t = 0; t <= 3; t++) TickBar(t, t, t, t * 100);
        TickBar(4, 4, 3, 300); TickBar(5, 5, 3, 300);
        for (int t = 6; t <= 10; t++) TickBar(t, t, t, 900);   // the jump lands at 6 or 7 (the figure lags a read)
        Assert.Equal([new RecordSpan(4, 7)], _r.Finish()!.Assumed);
    }

    [Fact]
    public void One_unread_second_is_left_out_and_a_bar_never_read_is_not()
    {
        for (int t = 0; t <= 3; t++) TickBar(t, t, t, t * 100);
        TickBar(4, 4, 3, 300); TickBar(5, 5, 3, 300);         // no floor
        TickBar(6, 6, 6, 300);
        Assert.Equal([new RecordSpan(4, 6)], _r.Finish()!.Assumed);

        for (int t = 20; t <= 25; t++) TickBar(t, t, 0, t * 100);   // a new record whose bar is never read
        Assert.Null(_r.Finish()!.Assumed);
    }

    [Fact]
    public void A_tinted_skill_bar_is_left_out_with_five_seconds_after_and_to_the_end_of_the_fight()
    {
        for (int t = 0; t <= 3; t++) TickBar(t, t, t, t * 100);
        TickBar(4, 4, 4, 400, tintAt: 4);                      // dead: the tint last seen at 4, fresh at 5 too
        for (int t = 5; t <= 10; t++) TickBar(t, t, t, 400, tintAt: 4);
        TickBar(11, 11, 11, 500, tintAt: 4);                   // 5 s past it: counted again
        TickBar(12, 12, 12, 500, tintAt: 12);                  // dead again, to the end
        Assert.Equal([new RecordSpan(3, 10), new RecordSpan(11, 12)], _r.Finish()!.Assumed);
    }

    [Fact]
    public void The_dead_time_of_a_support_is_not_left_out()
    {
        _r.Support = true;
        for (int t = 0; t <= 3; t++) TickBar(t, t, t, t * 100);
        TickBar(4, 4, 4, 400, tintAt: 4);
        TickBar(5, 5, 5, 500, tintAt: 4);
        Assert.Null(_r.Finish()!.Assumed);
    }

    [Fact]
    public void The_second_the_timer_next_moves_on_after_a_hidden_hud_is_left_out()
    {
        for (int t = 0; t <= 3; t++) TickBar(t, t, t, t * 100);
        _r.Step(MeterStatus.None with { Clock = _clock, HpAt = S(3) }, [], [], 300, "act3-g3", S(6), Wall);   // the timer unread, the bar too
        TickBar(10, 10, 10, 900);                              // 3 → 10 in one step
        TickBar(11, 11, 11, 1000);                             // the boss figure still settling
        Assert.Equal([new RecordSpan(3, 11)], _r.Finish()!.Assumed);
    }

    [Fact]
    public void Nothing_is_recorded_before_the_timer_moves()
    {
        Tick(5, 0);
        Cast("나", 0.2);
        Tick(5, 1);
        Assert.Null(_r.Finish());
    }

    [Fact]
    public void Recording_runs_from_the_timers_first_move_to_the_fights_end()
    {
        Tick(0, 0);
        Assert.False(_r.Recording);

        Tick(1, 1);
        Assert.True(_r.Recording);

        _r.Finish();
        Assert.False(_r.Recording);
    }

    [Fact]
    public void A_cast_between_moving_reads_is_kept_at_the_timer_plus_tenths()
    {
        Tick(0, 0); Tick(1, 1);
        Cast("나", 1.25);
        Tick(2, 2);
        var e = Assert.Single(_r.Finish()!.Events);
        Assert.Equal(("나", RecordEventKind.사용, (int)SlotId.일반3), (e.Name, e.Kind, e.What));
        Assert.Equal(1.3, e.T, 6);
    }

    [Fact]
    public void A_cast_after_the_last_move_is_dropped_when_the_timer_stops_and_the_restart_is_a_phase()
    {
        Tick(0, 0); Tick(1, 1); Tick(2, 2);
        Cast("나", 2.4);
        Tick(2, 3); Tick(2, 3.6);   // frozen: 1.6 s on 2
        Tick(3, 20); Tick(4, 21);
        var r = _r.Finish()!;
        Assert.Empty(r.Events);
        Assert.Equal([2.0], r.Phases);
        Assert.Equal(4, r.Duration);
    }

    [Fact]
    public void Samples_are_kept_once_a_second_while_the_timer_runs()
    {
        Tick(0, 0, Me(0)); Tick(1, 1, Me(100)); Tick(2, 2, Me(250)); Tick(2, 3.6, Me(250));
        var r = _r.Finish()!;
        Assert.Equal([(1.0, 100L), (2.0, 250L)], r.Samples.Select(s => (s.T, s.Damage)));
        Assert.Equal(2, r.Boss.Count);
        Assert.Equal(("act3-g3", Wall, "고독한기사"), (r.Gate, r.StartedAt, r.MySubclass));
        Assert.Equal("나", Assert.Single(r.Members).Name);
    }

    [Fact]
    public void A_new_record_returns_the_finished_one()
    {
        Tick(0, 0); Tick(1, 1); Tick(2, 2);
        Assert.Null(Tick(0, 3));   // one lower read waits for the next to agree (FightClock)
        var done = Tick(1, 4);
        Assert.Equal(2, done!.Duration);
        Assert.Null(Tick(2, 5));
    }

    [Fact]
    public void A_timer_stopped_two_minutes_finishes_the_fight()
    {
        Tick(0, 0); Tick(1, 1); Tick(1, 2.6);
        Assert.Null(Tick(1, 100));
        Assert.NotNull(Tick(1, 122));
        Assert.Null(_r.Finish());
    }

    [Fact]
    public void A_fight_past_the_size_cap_is_finished()
    {
        var r = new FightRecorder(maxEntries: 10) { Me = "나" };
        var clock = FightClockState.None;
        FightRecord? Step(int t)
        {
            clock = FightClock.Update(clock, t, S(t));
            return r.Step(MeterStatus.None with { Clock = clock }, [.. Enumerable.Range(0, 8).Select(i => Me(i) with { Name = $"p{i}" })], [], 0, null, S(t), Wall);
        }
        Step(0); Assert.Null(Step(1));
        Assert.NotNull(Step(2));
    }

    /// <summary>A snapshot with <paramref name="bomb"/> on the boss (15 s left) at <paramref name="at"/>, or none.</summary>
    static Snapshot Bombed(double at, Core.Vision.Bomb? bomb) => Snap(SlotPhase.Unknown, at) with
    {
        Amsu = bomb is { } k ? AmsuState.None with { Up = AmsuState.None.Up.Add(k, new BombTimer(S(at + 15), S(at))) } : AmsuState.None,
    };

    [Fact]
    public void A_bomb_on_the_boss_is_a_window_when_암수_is_on()
    {
        _r.AmsuOn = true;
        Tick(0, 0); Tick(1, 1);
        See("나", Bombed(1.1, null), 1.1);
        See("나", Bombed(1.2, Core.Vision.Bomb.빛암), 1.2);
        See("나", Bombed(1.7, null), 1.7);
        Tick(2, 2);
        var r = _r.Finish()!;
        Assert.True(r.AmsuOn);
        Assert.Equal([(RecordEventKind.암수시작, (int)Core.Vision.Bomb.빛암, 1.2), (RecordEventKind.암수끝, (int)Core.Vision.Bomb.빛암, 1.7)],
            r.Events.Select(e => (e.Kind, e.What, Math.Round(e.T, 6))));
    }

    [Fact]
    public void A_bomb_is_recorded_with_every_buff_bar_hidden()
    {
        _r.AmsuOn = true;   // no buff bar shown: the engine publishes no buffs at all
        Tick(0, 0); Tick(1, 1);
        See("나", Bombed(1.2, Core.Vision.Bomb.암수) with { Buffs = [] }, 1.2);
        See("나", Bombed(1.7, null) with { Buffs = [] }, 1.7);
        Tick(2, 2);
        Assert.Equal([RecordEventKind.암수시작, RecordEventKind.암수끝], _r.Finish()!.Events.Select(e => e.Kind));
    }

    [Fact]
    public void Without_암수_on_no_bomb_is_recorded()
    {
        Tick(0, 0); Tick(1, 1);
        See("나", Bombed(1.2, Core.Vision.Bomb.암수), 1.2);
        Tick(2, 2);
        var r = _r.Finish()!;
        Assert.False(r.AmsuOn);
        Assert.Empty(r.Events);
    }

    [Fact]
    public void A_bomb_that_ends_while_the_timer_is_stopped_ends_where_it_stopped()
    {
        _r.AmsuOn = true;
        Tick(0, 0); Tick(1, 1);
        See("나", Bombed(1.2, Core.Vision.Bomb.암수), 1.2);
        Tick(2, 2); Tick(2, 3.6);   // stopped at 2
        See("나", Bombed(4, null), 4);
        Tick(3, 10); Tick(4, 11);
        Assert.Equal([(RecordEventKind.암수시작, 1.2), (RecordEventKind.암수끝, 2.0)], _r.Finish()!.Events.Select(e => (e.Kind, Math.Round(e.T, 6))));
    }

    [Fact]
    public void My_buff_windows_are_kept()
    {
        Tick(0, 0); Tick(1, 1);
        See("나", Snap(SlotPhase.Unknown, 1.1), 1.1);
        See("나", Snap(SlotPhase.Unknown, 1.2, aden: true), 1.2);
        See("나", Snap(SlotPhase.Unknown, 1.7), 1.7);
        Tick(2, 2);
        Assert.Equal([(RecordEventKind.버프시작, 1.2), (RecordEventKind.버프끝, 1.7)],
            _r.Finish()!.Events.Select(e => (e.Kind, Math.Round(e.T, 6))));
    }

    [Fact]
    public void My_attack_buff_windows_are_kept_too()
    {
        Tick(0, 0); Tick(1, 1);
        See("나", Snap(SlotPhase.Unknown, 1.1), 1.1);
        See("나", Snap(SlotPhase.Unknown, 1.2, on: BuffKind.공증), 1.2);
        See("나", Snap(SlotPhase.Unknown, 1.7), 1.7);
        Tick(2, 2);
        Assert.Equal([(RecordEventKind.버프시작, (int)BuffKind.공증), (RecordEventKind.버프끝, (int)BuffKind.공증)],
            _r.Finish()!.Events.Select(e => (e.Kind, e.What)));
    }

    [Fact]
    public void A_buff_that_ends_while_the_timer_is_stopped_ends_where_it_stopped()
    {
        Tick(0, 0); Tick(1, 1);
        See("나", Snap(SlotPhase.Unknown, 1.1), 1.1);
        See("나", Snap(SlotPhase.Unknown, 1.2, aden: true), 1.2);
        Tick(2, 2); Tick(2, 3.6);   // stopped at 2
        See("나", Snap(SlotPhase.Unknown, 4), 4);
        Tick(3, 10); Tick(4, 11);
        Assert.Equal([(RecordEventKind.버프시작, 1.2), (RecordEventKind.버프끝, 2.0)], _r.Finish()!.Events.Select(e => (e.Kind, Math.Round(e.T, 6))));
    }

    [Fact]
    public void A_buff_already_on_when_recording_starts_starts_at_the_first_second()
    {
        Tick(0, 0);
        See("나", Snap(SlotPhase.Unknown, 0.5, aden: true), 0.5);
        Tick(1, 1); Tick(2, 2);
        var e = Assert.Single(_r.Finish()!.Events);
        Assert.Equal((RecordEventKind.버프시작, 0.0), (e.Kind, e.T));
    }

    [Fact]
    public void Casts_while_the_timer_goes_unread_are_dropped_not_piled_on_one_second()
    {
        Tick(0, 0); Tick(1, 1);
        Tick(null, 2); Tick(null, 3);
        Cast("나", 3.5);
        Tick(2, 4);
        Assert.Empty(_r.Finish()!.Events);
    }

    [Fact]
    public void An_unknown_slot_turning_to_cooldown_is_no_cast()
    {
        Tick(0, 0); Tick(1, 1);
        See("나", Snap(SlotPhase.Unknown, 1.1), 1.1);
        See("나", Snap(SlotPhase.Cooldown, 1.2), 1.2);
        Tick(2, 2);
        Assert.Empty(_r.Finish()!.Events);
    }

    // Step 3 (spec § D, the user's rules): start only in a raid; a clear ends at once (timer stopped, boss under 10,000);
    // a wipe or a hidden timer pauses, then ends after the pause length or on a restart (a new 전분 record).
    double _hpAt;
    long _max = 1_000_000_000_000;

    /// <summary>A second with the boss at <paramref name="hp"/>; <paramref name="read"/> false: the bar was not read this second
    /// (the boss's last HP held).</summary>
    FightRecord? TickHp(int? timer, double at, long hp, bool read = true)
    {
        _clock = FightClock.Update(_clock, timer, S(at));
        if (read) _hpAt = at;
        var boss = BossDamageState.None with { Last = hp, Max = _max };
        return _r.Step(MeterStatus.None with { Clock = _clock, Boss = boss, HpAt = S(_hpAt), TintAt = S(_tintAt), TintLast = _tintLast }, [Me(100)], [], 0, null, S(at), Wall);
    }

    // The skill bar's tint as the engine gives it: when it was last seen tinted, and whether its last sighting was.
    double _tintAt;
    bool _tintLast;
    const long Alive = 600_000_000_000;   // 60% of the boss: no kill

    /// <summary>A fight to 10 s, the skill bar tinted from 8 s on (dead), last seen at 10 s.</summary>
    void DieAt10()
    {
        for (int t = 0; t <= 10; t++)
        {
            if (t >= 8) (_tintAt, _tintLast) = (t, true);
            TickHp(t, t, Alive);
        }
    }

    [Fact]
    public void A_death_with_the_hud_gone_is_saved_as_a_fail_after_5_s()
    {
        DieAt10();
        for (int at = 11; at < 15; at++) Assert.Null(TickHp(null, at, Alive, read: false));
        var r = TickHp(null, 15, Alive, read: false);
        Assert.False(r!.Cleared);
        Assert.Equal(10, r.Duration);
    }

    [Fact]
    public void A_tinted_bar_still_on_screen_keeps_the_pause_length()
    {
        _r.PauseLimit = S(60);
        DieAt10();
        for (int at = 11; at < 70; at++) { _tintAt = at; Assert.Null(TickHp(10, at, Alive)); }   // held, the timer stopped
    }

    [Fact]
    public void A_bar_seen_untinted_since_keeps_the_pause_length()
    {
        _r.PauseLimit = S(60);
        DieAt10();
        _tintLast = false;   // up again, then a cutscene
        for (int at = 11; at < 70; at++) Assert.Null(TickHp(null, at, Alive, read: false));
    }

    [Fact]
    public void A_death_is_saved_on_time_when_the_same_status_is_stepped_again()
    {
        // Nothing on screen changes once the HUD and 전분 are gone, so the engine publishes no new status: the app steps the
        // last one again each second (App.Wiring, the recorder's own second).
        DieAt10();
        var last = MeterStatus.None with { Clock = _clock, Boss = BossDamageState.None with { Last = Alive, Max = _max }, HpAt = S(_hpAt), TintAt = S(_tintAt), TintLast = _tintLast };
        for (int at = 11; at < 15; at++) Assert.Null(_r.Step(last, [Me(100)], [], 0, null, S(at), Wall));
        Assert.False(_r.Step(last, [Me(100)], [], 0, null, S(15), Wall)!.Cleared);
    }

    [Fact]
    public void The_same_status_is_stepped_again_only_while_the_engine_reads()
    {
        Assert.True(FightRecorder.MayStepAgain(S(100), S(99.9), S(50)));    // reading, nothing changed on screen
        Assert.True(FightRecorder.MayStepAgain(S(100), S(98), S(50)));      // the edge: 2 s since the last read
        Assert.False(FightRecorder.MayStepAgain(S(100), S(97), S(50)));     // another app in front: the fight waits
        Assert.False(FightRecorder.MayStepAgain(S(100), TimeSpan.Zero, TimeSpan.Zero));   // nothing read yet
    }

    [Fact]
    public void Back_from_another_app_the_old_status_waits_until_a_new_one_can_be_read()
    {
        // Reading again from 99.5 s after two minutes away: the status from before still holds a timer that last moved
        // two minutes ago, so stepping it would end the fight. The first 2 s of reading publish a new one first.
        Assert.False(FightRecorder.MayStepAgain(S(100), S(99.9), S(99.5)));
        Assert.True(FightRecorder.MayStepAgain(S(101.5), S(101.4), S(99.5)));
    }

    [Fact]
    public void A_read_after_a_gap_starts_a_new_reading_stretch()
    {
        Assert.Equal(S(130), FightRecorder.ReadingSince(S(130), lastRead: S(10), since: S(5)));    // back after 2 min
        Assert.Equal(S(5), FightRecorder.ReadingSince(S(10.1), lastRead: S(10), since: S(5)));     // still reading
        Assert.Equal(S(3), FightRecorder.ReadingSince(S(3), lastRead: TimeSpan.Zero, since: TimeSpan.Zero));   // the first read
    }

    MeterStatus Last(long hp) => MeterStatus.None with { Clock = _clock, Boss = BossDamageState.None with { Last = hp, Max = _max }, HpAt = S(_hpAt) };

    [Fact]
    public void A_kill_is_saved_on_time_when_the_same_status_is_stepped_again()
    {
        // A timer stopped on screen gives the same status each read, so nothing new is published after the kill.
        _max = 2_000_000_000_000;
        for (int t = 0; t <= 10; t++) TickHp(t, t, t < 10 ? 500_000_000_000 : 98_000_000_000);
        TickHp(10, 11, 98_000_000_000);
        var last = Last(98_000_000_000);
        for (int at = 12; at < 15; at++) Assert.Null(_r.Step(last, [Me(100)], [], 0, null, S(at), Wall));
        Assert.True(_r.Step(last, [Me(100)], [], 0, null, S(15), Wall)!.Cleared);
    }

    [Fact]
    public void The_pause_limit_runs_out_on_time_when_the_same_status_is_stepped_again()
    {
        _r.PauseLimit = S(30);
        for (int t = 0; t <= 10; t++) TickHp(t, t, Alive);
        TickHp(10, 11, Alive);   // the timer stopped, the boss alive
        var last = Last(Alive);
        for (int at = 12; at < 40; at++) Assert.Null(_r.Step(last, [Me(100)], [], 0, null, S(at), Wall));
        Assert.NotNull(_r.Step(last, [Me(100)], [], 0, null, S(40), Wall));
    }

    [Fact]
    public void Stepping_the_same_status_again_in_a_running_fight_adds_nothing()
    {
        for (int t = 0; t <= 10; t++) TickHp(t, t, Alive);
        var last = MeterStatus.None with { Clock = _clock, Boss = BossDamageState.None with { Last = Alive, Max = _max }, HpAt = S(_hpAt) };
        Assert.Null(_r.Step(last, [Me(100)], [], 0, null, S(10.5), Wall));
        Assert.Null(_r.Step(last, [Me(100)], [], 0, null, S(11), Wall));
        var r = _r.Finish()!;
        Assert.Equal(r.Samples.Select(s => s.T).Distinct().Count(), r.Samples.Count);
        Assert.Null(r.Assumed);
    }

    [Fact]
    public void A_death_with_no_boss_hp_ever_read_is_a_fail()
    {
        FightRecord? Tick(int? timer, double at)
        {
            _clock = FightClock.Update(_clock, timer, S(at));
            return _r.Step(MeterStatus.None with { Clock = _clock, TintAt = S(_tintAt), TintLast = _tintLast }, [Me(100)], [], 0, null, S(at), Wall);
        }
        for (int t = 0; t <= 10; t++) { if (t >= 8) (_tintAt, _tintLast) = (t, true); Tick(t, t); }
        for (int at = 11; at < 15; at++) Assert.Null(Tick(null, at));
        Assert.False(Tick(null, 15)!.Cleared);
    }

    [Fact]
    public void A_fight_ended_from_outside_after_a_death_is_judged_as_before()
    {
        DieAt10();
        Assert.Null(TickHp(null, 12, Alive, read: false));
        Assert.False(_r.Finish()!.Cleared);   // the meter turned off: the boss alive and its bar just seen, 실패 as without a death
    }

    [Fact]
    public void A_support_s_death_is_saved_as_a_fail_too()
    {
        _r.Support = true;
        DieAt10();
        for (int at = 11; at < 15; at++) Assert.Null(TickHp(null, at, Alive, read: false));
        Assert.False(TickHp(null, 15, Alive, read: false)!.Cleared);
    }

    [Fact]
    public void A_tinted_bar_after_the_timer_was_seen_stopped_is_no_death()
    {
        _r.PauseLimit = S(60);
        DieAt10();
        for (int at = 11; at <= 14; at++) { _tintAt = at; TickHp(10, at, Alive); }   // the timer stopped on screen (a clear's tinted bar)
        for (int at = 15; at < 70; at++) Assert.Null(TickHp(null, at, Alive, read: false));   // then the loading screen
    }

    [Fact]
    public void A_tint_from_before_the_fight_is_no_death()
    {
        _r.PauseLimit = S(60);
        (_tintAt, _tintLast) = (0.5, true);   // the town's bar, not seen since
        for (int t = 1; t <= 10; t++) TickHp(t, t + 1, Alive);
        for (int at = 12; at < 70; at++) Assert.Null(TickHp(null, at, Alive, read: false));
    }

    [Fact]
    public void A_death_under_the_kill_line_is_still_a_clear()
    {
        _max = 2_000_000_000_000;
        for (int t = 0; t <= 10; t++) { if (t >= 8) (_tintAt, _tintLast) = (t, true); TickHp(t, t, 98_000_000_000); }
        for (int at = 11; at < 15; at++) TickHp(null, at, 98_000_000_000, read: false);
        Assert.True(TickHp(null, 15, 98_000_000_000, read: false)!.Cleared);
    }

    [Fact]
    public void The_time_between_a_death_save_and_the_record_going_on_is_left_out()
    {
        DieAt10();
        for (int at = 11; at <= 15; at++) TickHp(null, at, Alive, read: false);
        _tintLast = false;   // revived: 전분 ticks again
        for (int t = 30; t <= 33; t++) Assert.Null(TickHp(t, t + 10, Alive));
        Assert.Equal([new RecordSpan(10, 30)], _r.Finish()!.Assumed);
    }

    [Fact]
    public void A_new_record_after_a_death_save_leaves_nothing_out()
    {
        DieAt10();
        for (int at = 11; at <= 15; at++) TickHp(null, at, Alive, read: false);
        _tintLast = false;
        TickHp(0, 40, Alive); TickHp(1, 41, Alive);   // a retry: the timer starts over
        for (int t = 2; t <= 5; t++) TickHp(t, 40 + t, Alive);
        Assert.Null(_r.Finish()!.Assumed);
    }

    [Fact]
    public void Daenantu_s_start_and_end_are_on_the_fight_s_clock()
    {
        for (int t = 0; t <= 3; t++) TickHp(t, t, 500_000_000_000);
        _r.ShadowOn = true;
        for (int t = 4; t <= 6; t++) TickHp(t, t, 500_000_000_000);
        _r.ShadowOn = false;
        for (int t = 7; t <= 14; t++) TickHp(t, t, 500_000_000_000);
        var marks = _r.Finish()!.Events.Where(e => e.Kind is RecordEventKind.대난투입장 or RecordEventKind.대난투종료).ToList();
        Assert.Equal([(3d, RecordEventKind.대난투입장), (5d, RecordEventKind.대난투종료)], marks.Select(e => (e.T, e.Kind)));   // ended at the last second seen
    }

    [Fact]
    public void Daenantu_ending_just_before_the_fight_keeps_its_end()
    {
        for (int t = 0; t <= 2; t++) TickHp(t, t, 500_000_000_000);
        _r.ShadowOn = true;
        for (int t = 3; t <= 5; t++) TickHp(t, t, 500_000_000_000);
        _r.ShadowOn = false;
        for (int t = 6; t <= 7; t++) TickHp(t, t, 500_000_000_000);   // the kill 2 s after it
        var marks = _r.Finish()!.Events.Where(e => e.Kind is RecordEventKind.대난투입장 or RecordEventKind.대난투종료).ToList();
        Assert.Equal([(2d, RecordEventKind.대난투입장), (4d, RecordEventKind.대난투종료)], marks.Select(e => (e.T, e.Kind)));
    }

    [Fact]
    public void A_few_unread_seconds_inside_daenantu_do_not_end_it()
    {
        for (int t = 0; t <= 2; t++) TickHp(t, t, 500_000_000_000);
        _r.ShadowOn = true;
        for (int t = 3; t <= 5; t++) TickHp(t, t, 500_000_000_000);
        _r.ShadowOn = false;
        for (int t = 6; t <= 8; t++) TickHp(t, t, 500_000_000_000);   // 3 s unread
        _r.ShadowOn = true;
        for (int t = 9; t <= 11; t++) TickHp(t, t, 500_000_000_000);
        var marks = _r.Finish()!.Events.Where(e => e.Kind is RecordEventKind.대난투입장 or RecordEventKind.대난투종료).ToList();
        Assert.Equal([(2d, RecordEventKind.대난투입장)], marks.Select(e => (e.T, e.Kind)));   // still in it when the fight ended
    }

    [Fact]
    public void The_next_fight_starts_outside_daenantu()
    {
        for (int t = 0; t <= 2; t++) TickHp(t, t, 500_000_000_000);
        _r.ShadowOn = true;
        for (int t = 3; t <= 5; t++) TickHp(t, t, 500_000_000_000);
        _r.Finish();
        _r.ShadowOn = false;
        for (int t = 6; t <= 9; t++) TickHp(t, t, 500_000_000_000);
        Assert.DoesNotContain(_r.Finish()!.Events, e => e.Kind == RecordEventKind.대난투종료);
    }

    [Fact]
    public void A_boss_bar_gone_while_the_timer_runs_is_a_clear()
    {
        // 구원의 종탑 1관문 (2026-11-03): the bar's last read showed 1124억 left, then the boss died and the timer ran on 9 s.
        for (int t = 0; t <= 10; t++) TickHp(t, t, 112_393_993_321);
        for (int t = 11; t <= 15; t++) TickHp(t, t, 112_393_993_321, read: false);
        Assert.True(_r.Finish()!.Cleared);
    }

    [Fact]
    public void No_boss_hp_inside_daenantu_is_no_kill()
    {
        // 대난투 shows no boss HP at all, only the damage dealt (user, 2026-11-08): its number unread a few seconds (ShadowOn off,
        // no HP) and the fight ending then is not the bar-gone clear.
        for (int t = 0; t <= 2; t++) TickHp(t, t, 600_000_000_000);
        _r.ShadowOn = true;
        for (int t = 3; t <= 5; t++) TickHp(t, t, 600_000_000_000);
        _r.ShadowOn = false;
        for (int t = 6; t <= 9; t++) Assert.Null(TickHp(t, t, 600_000_000_000, read: false));
        Assert.False(_r.Finish()!.Cleared);
    }

    [Fact]
    public void A_bar_gone_after_daenantu_ended_is_a_clear_again()
    {
        for (int t = 0; t <= 2; t++) TickHp(t, t, 600_000_000_000);
        _r.ShadowOn = true;
        for (int t = 3; t <= 5; t++) TickHp(t, t, 600_000_000_000);
        _r.ShadowOn = false;
        for (int t = 6; t <= 20; t++) TickHp(t, t, 600_000_000_000, read: false);   // 대난투종료 at 5 (logged at 10), then the bar stays gone
        Assert.True(_r.Finish()!.Cleared);
    }

    [Fact]
    public void A_bar_missed_for_two_seconds_at_the_end_is_still_a_fail()
    {
        for (int t = 0; t <= 10; t++) TickHp(t, t, 600_000_000_000);
        for (int t = 11; t <= 12; t++) TickHp(t, t, 600_000_000_000, read: false);
        Assert.False(_r.Finish()!.Cleared);
    }

    [Fact]
    public void A_small_boss_is_a_clear_only_near_its_end()
    {
        // An older raid's 77억 boss: all of it is under 500억, so 5% of its max (3.9억) is the line.
        _max = 7_752_830_141;
        for (int t = 0; t <= 10; t++) TickHp(t, t, 1_000_000_000);
        Assert.False(_r.Finish()!.Cleared);
        for (int t = 20; t <= 30; t++) TickHp(t, t, 300_000_000);
        Assert.True(_r.Finish()!.Cleared);
    }

    [Fact]
    public void A_stopped_timer_ends_the_record_at_once_only_on_a_dead_boss()
    {
        // A phase change stops the timer under 5 s with a phase bar under the kill line: the fight goes on, it isn't cut there
        // (stopped 5 s it would be a kill, user 2026-11-11).
        for (int t = 0; t <= 10; t++) TickHp(t, t, 40_000_000_000);
        for (int at = 11; at <= 14; at++) Assert.Null(TickHp(10, at, 40_000_000_000));
        for (int t = 15; t <= 20; t++) Assert.Null(TickHp(t - 4, t, 600_000_000_000));
        Assert.Equal(16, _r.Finish()!.Duration);
    }

    [Fact]
    public void A_boss_last_read_under_500억_is_a_clear()
    {
        // A finishing skill can take 100~300억 in one hit (user, 2026-11-03): the last read may show that much left.
        for (int t = 0; t <= 10; t++) TickHp(t, t, 49_000_000_000);
        Assert.True(_r.Finish()!.Cleared);
    }

    [Fact]
    public void A_bar_gone_with_much_hp_left_is_still_a_clear()
    {
        // A gimmick kill with 65% left: the boss is gone, so is its bar.
        for (int t = 0; t <= 10; t++) TickHp(t, t, 600_000_000_000);
        for (int t = 11; t <= 20; t++) TickHp(t, t, 600_000_000_000, read: false);
        Assert.True(_r.Finish()!.Cleared);
    }

    [Fact]
    public void A_wipe_near_the_end_is_a_fail_as_its_bar_stays()
    {
        // 공략에 실패하였습니다 (종막, user 2026-11-03): the bar stays up and read.
        for (int t = 0; t <= 20; t++) TickHp(t, t, 600_000_000_000);
        Assert.False(_r.Finish()!.Cleared);
    }

    [Fact]
    public void A_record_ended_by_a_new_전분_record_says_so()
    {
        for (int t = 0; t <= 20; t++) TickHp(t, t, 600_000_000_000);
        Assert.Null(TickHp(2, 21, 600_000_000_000));   // the lower timer waits a read to confirm the new record
        Assert.NotNull(TickHp(3, 22, 600_000_000_000));
        Assert.True(_r.Restarted);
        TickHp(4, 23, 600_000_000_000);
        Assert.NotNull(_r.Finish());
        Assert.False(_r.Restarted);
    }

    [Fact]
    public void Outside_a_raid_nothing_is_recorded()
    {
        _r.InRaid = false;
        for (int t = 0; t < 5; t++) Tick(t, t);
        Assert.Null(_r.Finish());
    }

    [Fact]
    public void A_clear_ends_the_record_as_soon_as_the_timer_stops()
    {
        for (int t = 0; t <= 10; t++) Assert.Null(TickHp(t, t, t < 8 ? 1_000_000_000 : 1));
        Assert.Null(TickHp(10, 11, 1));
        var r = TickHp(10, 12, 1);   // frozen: 1.5 s without moving
        Assert.NotNull(r);
        Assert.True(r!.Cleared);
    }

    [Fact]
    public void A_boss_under_the_kill_line_with_the_timer_stopped_ends_the_record_after_5_s()
    {
        // The last read lags the kill (0.1-1.2% on the user's 33 clears), so the 10,000 rule never ends one at once: the fight
        // stayed open 2 min and was saved under the next character (user, 2026-11-11). 4.9% of 2조: over 500억, no cap.
        _max = 2_000_000_000_000;
        for (int t = 0; t <= 10; t++) TickHp(t, t, t < 10 ? 500_000_000_000 : 98_000_000_000);
        for (int at = 11; at < 15; at++) Assert.Null(TickHp(10, at, 98_000_000_000));
        var r = TickHp(10, 15, 98_000_000_000);
        Assert.NotNull(r);
        Assert.True(r!.Cleared);
    }

    [Fact]
    public void A_stop_over_the_kill_line_keeps_the_pause_length()
    {
        const long hp = 60_000_000_000;   // 6% of 1조: a phase change or a wipe
        _r.PauseLimit = S(60);
        for (int t = 0; t <= 10; t++) TickHp(t, t, hp);
        for (int at = 11; at < 70; at++) Assert.Null(TickHp(10, at, hp));
    }

    [Fact]
    public void A_wipe_pauses_then_ends_as_a_fail_after_the_pause_length()
    {
        _r.PauseLimit = S(30);
        for (int t = 0; t <= 10; t++) TickHp(t, t, 600_000_000_000);
        for (int at = 11; at < 40; at++) Assert.Null(TickHp(10, at, 600_000_000_000));
        var r = TickHp(10, 40, 600_000_000_000);
        Assert.False(r!.Cleared);
    }

    [Fact]
    public void A_hidden_timer_ends_the_record_after_the_pause_length_too()
    {
        _r.PauseLimit = S(30);
        for (int t = 0; t <= 10; t++) TickHp(t, t, 600_000_000_000);
        for (int at = 11; at < 40; at++) Assert.Null(TickHp(null, at, 600_000_000_000));
        Assert.NotNull(TickHp(null, 40, 600_000_000_000));
    }

    [Fact]
    public void A_cutscene_shorter_than_the_pause_resumes_the_same_record()
    {
        _r.PauseLimit = S(30);
        for (int t = 0; t <= 10; t++) TickHp(t, t, 600_000_000_000);
        for (int at = 11; at < 30; at++) Assert.Null(TickHp(null, at, 600_000_000_000));
        for (int t = 30; t <= 35; t++) Assert.Null(TickHp(t, t, 550_000_000_000));
        Assert.Equal(35, _r.Finish()!.Duration);
    }

    [Fact]
    public void A_restart_saves_the_wipe_and_the_next_try_is_a_new_record()
    {
        for (int t = 0; t <= 148; t++) TickHp(t, t, 600_000_000_000);
        for (int at = 149; at < 170; at++) TickHp(148, at, 600_000_000_000);   // frozen through the preparation
        Assert.Null(TickHp(3, 170, 600_000_000_000));   // re-entered: the next read confirms the reset
        var r = TickHp(4, 171, 600_000_000_000);
        Assert.False(r!.Cleared);
        Assert.Equal(148, r.Duration);
    }

    [Fact]
    public void A_timer_waiting_before_the_fight_is_not_a_phase()
    {
        for (int at = 0; at < 5; at++) TickHp(0, at, 600_000_000_000);   // frozen at 0 in the preparation
        for (int t = 1; t <= 5; t++) TickHp(t, 4 + t, 600_000_000_000);
        Assert.Empty(_r.Finish()!.Phases);
    }

    [Fact]
    public void A_buff_change_while_the_timer_is_hidden_is_recorded_when_it_shows_again()
    {
        Tick(0, 0); Tick(1, 1); Tick(2, 2);
        Tick(null, 3); Tick(null, 4);                       // the panel covered: the timer unread
        See("나", Snap(SlotPhase.Ready, 4.5, aden: true), 4.5);   // 아덴 피증 on meanwhile: no second to put it at
        Tick(null, 5);
        Tick(6, 6);                                         // shown again
        See("나", Snap(SlotPhase.Ready, 6.5, aden: false), 6.5);
        Tick(7, 7); Tick(8, 8);
        var buffs = _r.Finish()!.Events.Where(e => e.Kind != RecordEventKind.사용).Select(e => (e.T, e.Kind)).ToList();
        Assert.Equal([(2.0, RecordEventKind.버프시작), (6.5, RecordEventKind.버프끝)], buffs);   // on at the last second known
    }

    [Fact]
    public void A_buff_back_on_after_a_late_timer_read_stays_on_after_its_end()
    {
        // 구원의 종탑 2관문 (2026-11-09): 공증 off at 107.11, on again while the timer read lagged (the edge dropped), put back at
        // 107 by the catch-up: sorted, 공증 read off for 80 s.
        Tick(0, 0); Tick(1, 1);
        See("나", Snap(SlotPhase.Ready, 1.2, aden: true), 1.2);
        Tick(2, 2);
        See("나", Snap(SlotPhase.Ready, 2.1, aden: false), 2.1);   // off at 2.1
        See("나", Snap(SlotPhase.Ready, 3.6, aden: true), 3.6);    // on again 1.6 s after the timer moved: dropped
        Tick(3, 3.7); Tick(4, 4.7);
        var buffs = _r.Finish()!.Events.Where(e => e.Kind != RecordEventKind.사용).OrderBy(e => e.T).Select(e => e.Kind).ToList();
        Assert.Equal([RecordEventKind.버프시작, RecordEventKind.버프끝, RecordEventKind.버프시작], buffs);
    }

    [Fact]
    public void A_buff_change_still_waiting_for_its_second_is_not_recorded_twice()
    {
        Tick(0, 0); Tick(1, 1);
        See("나", Snap(SlotPhase.Ready, 1.4, aden: true), 1.4);   // waits for the timer to pass 1.4
        Tick(2, 2); Tick(3, 3);
        Assert.Single(_r.Finish()!.Events, e => e.Kind == RecordEventKind.버프시작);
    }
}
