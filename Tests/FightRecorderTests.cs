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

    [Fact]
    public void Nothing_is_recorded_before_the_timer_moves()
    {
        Tick(5, 0);
        Cast("나", 0.2);
        Tick(5, 1);
        Assert.Null(_r.Finish());
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
    FightRecord? TickHp(int? timer, double at, long hp)
    {
        _clock = FightClock.Update(_clock, timer, S(at));
        var boss = BossDamageState.None with { Last = hp, Max = 7_752_830_141 };
        return _r.Step(MeterStatus.None with { Clock = _clock, Boss = boss }, [Me(100)], [], 0, null, S(at), Wall);
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
    public void A_wipe_pauses_then_ends_as_a_fail_after_the_pause_length()
    {
        _r.PauseLimit = S(30);
        for (int t = 0; t <= 10; t++) TickHp(t, t, 5_000_000_000);
        for (int at = 11; at < 40; at++) Assert.Null(TickHp(10, at, 5_000_000_000));
        var r = TickHp(10, 40, 5_000_000_000);
        Assert.False(r!.Cleared);
    }

    [Fact]
    public void A_hidden_timer_ends_the_record_after_the_pause_length_too()
    {
        _r.PauseLimit = S(30);
        for (int t = 0; t <= 10; t++) TickHp(t, t, 5_000_000_000);
        for (int at = 11; at < 40; at++) Assert.Null(TickHp(null, at, 5_000_000_000));
        Assert.NotNull(TickHp(null, 40, 5_000_000_000));
    }

    [Fact]
    public void A_cutscene_shorter_than_the_pause_resumes_the_same_record()
    {
        _r.PauseLimit = S(30);
        for (int t = 0; t <= 10; t++) TickHp(t, t, 5_000_000_000);
        for (int at = 11; at < 30; at++) Assert.Null(TickHp(null, at, 5_000_000_000));
        for (int t = 30; t <= 35; t++) Assert.Null(TickHp(t, t, 4_000_000_000));
        Assert.Equal(35, _r.Finish()!.Duration);
    }

    [Fact]
    public void A_restart_saves_the_wipe_and_the_next_try_is_a_new_record()
    {
        for (int t = 0; t <= 148; t++) TickHp(t, t, 5_000_000_000);
        for (int at = 149; at < 170; at++) TickHp(148, at, 5_000_000_000);   // frozen through the preparation
        Assert.Null(TickHp(3, 170, 5_000_000_000));   // re-entered: the next read confirms the reset
        var r = TickHp(4, 171, 5_000_000_000);
        Assert.False(r!.Cleared);
        Assert.Equal(148, r.Duration);
    }

    [Fact]
    public void A_timer_waiting_before_the_fight_is_not_a_phase()
    {
        for (int at = 0; at < 5; at++) TickHp(0, at, 5_000_000_000);   // frozen at 0 in the preparation
        for (int t = 1; t <= 5; t++) TickHp(t, 4 + t, 5_000_000_000);
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
    public void A_buff_change_still_waiting_for_its_second_is_not_recorded_twice()
    {
        Tick(0, 0); Tick(1, 1);
        See("나", Snap(SlotPhase.Ready, 1.4, aden: true), 1.4);   // waits for the timer to pass 1.4
        Tick(2, 2); Tick(3, 3);
        Assert.Single(_r.Finish()!.Events, e => e.Kind == RecordEventKind.버프시작);
    }
}
