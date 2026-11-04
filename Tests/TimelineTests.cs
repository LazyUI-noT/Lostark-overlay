using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The timeline's layout (spec § D Timeline view): the time window under zoom and scrub, lanes, what is visible, my
/// buff windows, and where 비교 lines two records up.</summary>
public class TimelineTests
{
    static RecordEvent E(double t, string name, RecordEventKind kind = RecordEventKind.사용, int what = 0) => new(t, name, kind, what);

    static FightRecord R(double duration, IReadOnlyList<double>? phases = null, params RecordEvent[] events) =>
        new(DateTime.MinValue, null, duration, phases ?? [], "나", 0, null,
            [new RecordMember("철수", 0, MeterRole.딜러, 2), new RecordMember("민수", 0, MeterRole.딜러, 1), new RecordMember("나", 0, MeterRole.딜러, 1)],
            [], [], events);

    [Fact]
    public void Zooming_keeps_the_second_under_the_cursor()
    {
        var w = new TimeWindow(0, 100).Zoom(0.5, at: 80, length: 100);
        Assert.Equal((40, 90), (w.From, w.To));
        Assert.Equal(new TimeWindow(0, 100).X(80, 1000), w.X(80, 1000) * 1, 6);   // 800 px either way
    }

    [Fact]
    public void Zoom_and_scrub_stay_within_the_record()
    {
        Assert.Equal(new TimeWindow(0, 100), new TimeWindow(40, 60).Zoom(10, at: 50, length: 100));
        Assert.Equal(5, new TimeWindow(40, 60).Zoom(0.01, at: 50, length: 100) is var z ? z.To - z.From : 0, 6);
        Assert.Equal(new TimeWindow(80, 100), new TimeWindow(70, 90).Pan(30, length: 100));
        Assert.Equal(new TimeWindow(0, 20), new TimeWindow(10, 30).Pan(-30, length: 100));
    }

    [Fact]
    public void Only_events_in_the_window_are_drawn() =>
        Assert.Equal([2.0, 3.0], Timeline.Visible(R(10, null, E(1, "나"), E(2, "나"), E(3, "나"), E(4, "나")), new TimeWindow(2, 3)).Select(e => e.T));

    [Fact]
    public void Buff_windows_pair_start_and_end_and_an_open_one_runs_to_the_end()
    {
        var r = R(60, null,
            E(5, "나", RecordEventKind.버프시작, (int)BuffKind.아덴피증), E(9, "나", RecordEventKind.버프끝, (int)BuffKind.아덴피증),
            E(7, "나", RecordEventKind.버프시작, (int)BuffKind.초각스피증), E(50, "나", RecordEventKind.버프시작, (int)BuffKind.아덴피증));
        Assert.Equal([(5.0, 9.0, BuffKind.아덴피증), (7.0, 60.0, BuffKind.초각스피증), (50.0, 60.0, BuffKind.아덴피증)], Timeline.BuffSpans(r));
    }

    [Fact]
    public void Comparing_at_a_phase_the_record_lacks_falls_back_to_the_start()
    {
        var r = R(300, [120.5]);
        Assert.Equal(0, Timeline.Offset(r, 0));
        Assert.Equal(120.5, Timeline.Offset(r, 1));
        Assert.Equal(0, Timeline.Offset(r, 2));
        Assert.Equal(0, Timeline.Offset(r, -1));
    }

    [Fact]
    public void My_skills_are_the_slots_I_used_in_bar_order()
    {
        var r = R(60, null, E(1, "나", what: (int)SlotId.일반3), E(2, "나", what: (int)SlotId.각성기), E(3, "나", what: (int)SlotId.일반3),
            E(4, "나", RecordEventKind.버프시작, (int)BuffKind.아덴피증), E(5, "민수", what: (int)SlotId.일반1));
        Assert.Equal([SlotId.각성기, SlotId.일반3], Timeline.MySkills(r));
    }

    [Fact]
    public void Only_the_same_gate_and_difficulty_compare()
    {
        var a = R(60) with { Gate = "고통의 마녀 · 1관문", Difficulty = "하드" };
        Assert.True(Timeline.SameRaid(a, a with { StartedAt = DateTime.MaxValue }));
        Assert.False(Timeline.SameRaid(a, a with { Difficulty = "나이트메어" }));
        Assert.False(Timeline.SameRaid(a, a with { Gate = "고통의 마녀 · 2관문" }));
        Assert.False(Timeline.SameRaid(a with { Gate = null }, a with { Gate = null }));   // 알 수 없음 is no raid to match
    }

    /// <summary>A fight of 120 s on a bar of 2,000,000, losing 100 a second but for <paramref name="rate"/>'s seconds.</summary>
    static FightRecord BossFight(Func<int, long>? rate = null, Func<int, long>? max = null)
    {
        long taken = 0;
        var boss = new List<RecordBoss>();
        for (int t = 0; t <= 120; t++)
        {
            if (t > 0) taken += rate?.Invoke(t) ?? 100;
            boss.Add(new RecordBoss(t, taken, 2_000_000 - taken, max?.Invoke(t) ?? 2_000_000_000));
        }
        return new(new DateTime(2026, 11, 4, 20, 0, 0), "벨가르딘 · 2관문", 120, [], "나", 3, null, [], [], boss, []);
    }

    static long Stall(int t, int from, int to, long during = 0) => t > from && t <= to ? during : 100;

    [Fact]
    public void Daenantu_marks_are_the_recorded_ones_else_a_shadow_gate_s_timer_stops()
    {
        var r = BossFight() with { Phases = [100] };
        Assert.Equal([(100d, true)], Timeline.GimmickMarks(r, shadowGate: true));
        Assert.Empty(Timeline.GimmickMarks(r, shadowGate: false));
        var recorded = r with { Events = [new(90, "나", RecordEventKind.대난투입장, 0), new(110, "나", RecordEventKind.대난투종료, 0)] };
        Assert.Equal([(90d, true), (110d, false)], Timeline.GimmickMarks(recorded, shadowGate: true));
    }

    [Fact]
    public void Daenantu_zones_run_from_start_to_end_or_the_fight_s_end()
    {
        RecordEvent In(double t) => new(t, "나", RecordEventKind.대난투입장, 0);
        RecordEvent Out(double t) => new(t, "나", RecordEventKind.대난투종료, 0);
        Assert.Equal([(10d, 20d), (60d, 120d)], Timeline.GimmickZones(BossFight() with { Events = [In(10), Out(20), In(60)] }));
        Assert.Empty(Timeline.GimmickZones(BossFight() with { Phases = [100] }));   // an older fight: its end isn't known
        Assert.Empty(Timeline.GimmickZones(BossFight() with { Events = [Out(5)] }));
    }

    [Fact]
    public void Tags_keep_their_height_when_nothing_is_in_the_way() =>
        Assert.Equal([50d, 20d, 90d], Timeline.SpreadTags([50, 20, 90], 10, 0, 200));

    [Fact]
    public void Tags_too_close_are_pushed_apart_keeping_their_order() =>
        Assert.Equal([40d, 50d, 60d], Timeline.SpreadTags([40, 41, 42], 10, 0, 200));

    [Fact]
    public void Tags_stay_inside_the_chart()
    {
        Assert.Equal([5d, 15d], Timeline.SpreadTags([0, 1], 10, 5, 200));
        Assert.Equal([180d, 190d, 200d], Timeline.SpreadTags([195, 198, 199], 10, 0, 200));
    }

    [Fact]
    public void No_tags_place_nothing() => Assert.Empty(Timeline.SpreadTags([], 10, 0, 200));

    [Fact]
    public void No_unhittable_inside_daenantu()
    {
        Assert.Equal([(10d, 20d), (60d, 70d)], Timeline.Outside([(10, 70)], [(20, 60)]));
        Assert.Empty(Timeline.Outside([(30, 40)], [(20, 60)]));
        Assert.Empty(Timeline.Outside([(17, 30)], [(20, 60)]));   // 3 s left before it: under 5 s
        Assert.Equal([(80d, 90d)], Timeline.Outside([(80, 90)], [(20, 60)]));
    }

    [Fact]
    public void An_older_fight_s_daenantu_runs_from_its_entry_to_the_end()
    {
        var r = BossFight() with { Phases = [100] };
        Assert.Equal([(100d, 120d)], Timeline.GimmickSpans(r, Timeline.GimmickMarks(r, shadowGate: true)));
        Assert.Empty(Timeline.GimmickSpans(r, []));
        var recorded = r with { Events = [new(90, "나", RecordEventKind.대난투입장, 0), new(110, "나", RecordEventKind.대난투종료, 0)] };
        Assert.Equal([(90d, 111d)], Timeline.GimmickSpans(recorded, Timeline.GimmickMarks(recorded, shadowGate: true)));   // to the bar's next read
    }

    [Fact]
    public void Until_the_boss_bar_is_read_again_after_daenantu_is_not_unhittable()
    {
        // 대난투 90–110, then no HP read until the bar is back at 118 (user, 2026-11-05): not 보스 타격 불가.
        // The bar comes back with the HP the gimmick left: nothing lost over the gap, a stall without the cut.
        var r = BossFight(t => Stall(t, 110, 118)) with { Events = [new(90, "나", RecordEventKind.대난투입장, 0), new(110, "나", RecordEventKind.대난투종료, 0)] };
        r = r with { Boss = [.. r.Boss.Where(b => b.T <= 110 || b.T >= 118)] };
        Assert.Equal([(110d, 118d)], Timeline.BossUnhittable(r, _ => Line));
        var spans = Timeline.GimmickSpans(r, Timeline.GimmickMarks(r, shadowGate: true));
        Assert.Equal([(90d, 118d)], spans);
        Assert.Empty(Timeline.Outside(Timeline.BossUnhittable(r, _ => Line), spans));
    }

    const double Line = 50;   // under a second of this fight's normal damage, so a band ends where the stall does

    static IReadOnlyList<(double From, double To)> Unhittable(FightRecord r) => Timeline.BossUnhittable(r, _ => Line);

    [Fact]
    public void Under_one_line_lost_in_five_seconds_is_unhittable() =>
        Assert.Equal([(40d, 50d)], Unhittable(BossFight(t => Stall(t, 40, 50))));

    [Fact]
    public void Under_five_seconds_is_not_unhittable() =>
        Assert.Empty(Unhittable(BossFight(t => Stall(t, 40, 44))));

    [Fact]
    public void A_trickle_under_a_line_still_counts() =>
        Assert.Equal([(40d, 50d)], Unhittable(BossFight(t => Stall(t, 40, 50, during: 2))));

    [Fact]
    public void A_line_or_more_in_five_seconds_is_hittable() =>
        Assert.Empty(Unhittable(BossFight(t => Stall(t, 40, 50, during: 10))));

    [Fact]
    public void A_long_stall_does_not_run_on_into_the_damage_after_it() =>
        Assert.Equal([(20d, 80d)], Unhittable(BossFight(t => Stall(t, 20, 80))));

    [Fact]
    public void Each_stall_is_found() =>
        Assert.Equal([(20d, 30d), (70d, 90d)], Unhittable(BossFight(t => t is > 20 and <= 30 or > 70 and <= 90 ? 0 : 100)));

    [Fact]
    public void A_bar_showing_question_marks_is_not_judged_but_what_comes_before_it_is()
    {
        var hidden = BossFight(t => Stall(t, 60, 70), max: t => t > 70 && t < 100 ? 7 : 2_000_000_000);
        Assert.Equal([(60d, 70d)], Unhittable(hidden));

        var all = BossFight(t => t > 60 ? 0 : 100, max: t => t > 60 ? 7 : 2_000_000_000);
        Assert.Empty(Unhittable(all));
    }

    [Fact]
    public void The_stall_a_fight_ends_on_is_left_out() =>   // a dead boss or a wiped party, not an unhittable boss
        Assert.Empty(Unhittable(BossFight(t => t > 110 ? 0 : 100)));

    [Fact]
    public void No_boss_reads_nothing_unhittable() =>
        Assert.Empty(Unhittable(BossFight() with { Boss = [] }));

    [Fact]
    public void A_gate_without_a_line_count_is_not_judged() =>
        Assert.Empty(Timeline.BossUnhittable(BossFight(t => Stall(t, 40, 50)), _ => null));

    [Fact]
    public void A_line_is_the_size_of_the_bar_being_read()
    {
        var two = BossFight(t => Stall(t, 80, 90, during: 2), max: t => t <= 60 ? 1_000_000_000 : 2_000_000_000);
        // 10 lost in 5 s: under a line of 11 on the second bar; a first bar with its line of 5 finds nothing.
        Assert.Equal([(80d, 90d)], Timeline.BossUnhittable(two, max => max == 2_000_000_000 ? 11 : 5));
    }

    [Fact]
    public void The_seam_of_a_joined_run_is_not_unhittable()
    {
        var r = BossFight();
        r = r with { Boss = [.. r.Boss.Select(b => b.T > 60 ? b with { Taken = b.Taken - 6000 } : b)] };   // the next file counts from 0 again
        Assert.Empty(Unhittable(r));
    }

    [Fact]
    public void A_read_without_a_max_keeps_the_last_line()
    {
        var r = BossFight(t => Stall(t, 40, 50));
        r = r with { Boss = [.. r.Boss.Select(b => b.T is > 30 and < 60 ? b with { Max = null } : b)] };
        Assert.Equal([(40d, 50d)], Unhittable(r));
    }

    [Fact]
    public void An_icon_is_one_second_wide_between_its_smallest_and_its_largest()
    {
        Assert.Equal(20, Timeline.IconSize(pxPerSecond: 20, min: 8, max: 52));   // 1 s at 20 px a second
        Assert.Equal(16, Timeline.IconSize(pxPerSecond: 1.8, min: 16, max: 52));   // a whole 6-minute fight: never under the normal size
        Assert.Equal(52, Timeline.IconSize(pxPerSecond: 80, min: 8, max: 52));
    }

    [Fact]
    public void Fully_zoomed_the_largest_icon_is_one_second()
    {
        double span = Timeline.NarrowestSpan(plotWidth: 700, maxIcon: 52);
        Assert.Equal(52, Timeline.IconSize(700 / span, 8, 52), 6);
        Assert.Equal(700 / 52.0, span, 6);
        Assert.Equal(TimeWindow.MinSpan, Timeline.NarrowestSpan(plotWidth: 50, maxIcon: 52));   // never under the plain minimum
    }

    [Fact]
    public void The_narrowest_zoom_stops_the_window()
    {
        var w = new TimeWindow(0, 300).Zoom(0.01, at: 100, length: 300, minSpan: 27);
        Assert.Equal(27, w.Span, 6);
        var shortFight = TimeWindow.Whole(30).Zoom(0.8, at: 10, length: 30, minSpan: 64);   // shorter than the narrowest zoom
        Assert.Equal(new TimeWindow(0, 30), shortFight);   // zooming in never widens past the fight
    }

    [Fact]
    public void Close_casts_take_the_three_lanes_in_turn_at_the_normal_size()
    {
        var places = Timeline.IconLanes([0, 5, 10, 15, 40], size: 16, gap: 2, full: 52);
        Assert.Equal([0d, 18d, 36d, 0d, 0d], places.Select(p => p.Top));   // the fourth over the oldest; the fifth has room
        Assert.All(places, p => Assert.Equal(16, p.Size));
    }

    [Fact]
    public void Larger_icons_leave_fewer_lanes_centred_in_the_row()
    {
        Assert.Equal([(0d, 52d), (0d, 52d)], Timeline.IconLanes([0, 100], size: 52, gap: 2, full: 52));
        var two = Timeline.IconLanes([0, 5], size: 25, gap: 2, full: 52);
        Assert.Equal([0d, 27d], two.Select(p => p.Top));   // two lanes of 25
    }

    [Fact]
    public void The_icons_under_the_line_are_every_cast_whose_icon_it_crosses()
    {
        double[] lefts = [10, 20, 25, 100];   // icons 20 px wide: 10–30, 20–40, 25–45 overlap
        Assert.Equal([0], Timeline.IconsUnder(lefts, 20, 15));
        Assert.Equal([0, 1, 2], Timeline.IconsUnder(lefts, 20, 28));
        Assert.Empty(Timeline.IconsUnder(lefts, 20, 60));
    }

    [Fact]
    public void Amsu_uptime_is_the_bombs_time_over_the_window()
    {
        RecordEvent E(double t, RecordEventKind k) => new(t, "나", k, 0);
        var r = BossFight() with { AmsuOn = true, Events = [E(10, RecordEventKind.암수시작), E(40, RecordEventKind.암수끝), E(100, RecordEventKind.암수시작)] };
        Assert.Equal(50 / 120d, Timeline.AmsuUptime(r, new TimeWindow(0, 120)), 6);   // 10–40 and 100 to the end (120)
        Assert.Equal(0.5, Timeline.AmsuUptime(r, new TimeWindow(20, 60)), 6);
    }

    [Fact]
    public void Buff_share_is_the_part_of_my_damage_in_the_window_dealt_under_it()
    {
        // 100 damage a second from 0 to 20 s, none after: the buff's 4 s (5–9) take 400, the open one from 50 s nothing.
        var r = R(60, null,
            E(5, "나", RecordEventKind.버프시작, (int)BuffKind.아덴피증), E(9, "나", RecordEventKind.버프끝, (int)BuffKind.아덴피증),
            E(50, "나", RecordEventKind.버프시작, (int)BuffKind.아덴피증)) with
        {
            Samples = [new(0, "나", 0, 0), new(20, "나", 2000, 100), new(60, "나", 2000, 0), new(30, "철수", 99_999, 0)],
        };
        Assert.Equal(400.0 / 2000, Timeline.BuffShare(r, BuffKind.아덴피증, new TimeWindow(0, 60)), 6);
        Assert.Equal(400.0 / 1000, Timeline.BuffShare(r, BuffKind.아덴피증, new TimeWindow(0, 10)), 6);
        Assert.Equal(0, Timeline.BuffShare(r, BuffKind.초각스피증, new TimeWindow(0, 60)));
        Assert.Equal(0, Timeline.BuffShare(r, BuffKind.아덴피증, new TimeWindow(40, 60)));   // no damage in it
        Assert.Equal(0, Timeline.BuffShare(r, BuffKind.아덴피증, new TimeWindow(20, 20)));
    }

    [Fact]
    public void Party_buff_share_is_the_part_of_the_boss_hp_lost_under_my_buff()
    {
        // The boss loses 10M a second from 0 to 20 s: 공증 from 5 to 9 s takes 40M of 200M. Mine is a different curve.
        var r = R(60, null, E(5, "나", RecordEventKind.버프시작, (int)BuffKind.공증), E(9, "나", RecordEventKind.버프끝, (int)BuffKind.공증)) with
        {
            Samples = [new(0, "나", 0, 0), new(5, "나", 0, 0), new(9, "나", 1000, 0), new(20, "나", 1000, 0)],
            Boss = [new(0, 0, null, null), new(20, 200_000_000, null, null), new(60, 200_000_000, null, null)],
        };
        Assert.Equal(0.2, Timeline.PartyBuffShare(r, BuffKind.공증, new TimeWindow(0, 60))!.Value, 6);
        Assert.Equal(1.0, Timeline.BuffShare(r, BuffKind.공증, new TimeWindow(0, 60)), 6);
        Assert.Null(Timeline.PartyBuffShare(r with { Boss = [] }, BuffKind.공증, new TimeWindow(0, 60)));   // no boss read
    }

    [Fact]
    public void Dps_averages_put_me_first_then_the_highest()
    {
        var r = R(60) with
        {
            Samples = [new(1, "철수", 0, 50), new(1, "나", 0, 100), new(2, "나", 0, 200), new(1, "민수", 0, 300), new(2, "민수", 0, 300), new(30, "민수", 0, 900)],
        };
        Assert.Equal([("나", 150.0), ("민수", 300.0), ("철수", 50.0)], Timeline.DpsAverages(r, new TimeWindow(0, 10)));
    }

    [Fact]
    public void A_zero_length_record_still_has_a_window() =>
        Assert.True(TimeWindow.Whole(0).To > TimeWindow.Whole(0).From);

    /// <summary>A 40 s fight: my 피해량 and the boss's Taken grow only from 10 to 30 s (1조 and 2조), a bomb up from 10 to 30 s.</summary>
    static FightRecord Bombed(bool amsuOn, double from = 10, double to = 30)
    {
        double[] ts = [0, 10, 20, 30, 40];
        long Grown(double t, long total) => t <= 10 ? 0 : t >= 30 ? total : (long)(total * (t - 10) / 20);
        return R(40, null, E(from, "나", RecordEventKind.암수시작, 0), E(to, "나", RecordEventKind.암수끝, 0)) with
        {
            Samples = [.. ts.Select(t => new RecordSample(t, "나", Grown(t, 1_000_000_000_000), 0))],
            Boss = [.. ts.Select(t => new RecordBoss(t, Grown(t, 2_000_000_000_000), null, null))],
            AmsuOn = amsuOn,
        };
    }

    [Fact]
    public void 암수_gain_is_10_35_percent_of_the_damage_done_while_a_bomb_was_up()
    {
        var (mine, total, share) = Timeline.AmsuGain(Bombed(amsuOn: true))!.Value;
        Assert.Equal(103_500_000_000, mine, 0);    // 1조 × 10.35% = 1035억
        Assert.Equal(207_000_000_000, total, 0);   // the boss's 2조 likewise
        Assert.Equal(0.1035, share, 6);            // all my 1조 was under the bomb
    }

    [Fact]
    public void The_암수_line_is_the_part_of_my_damage_dealt_under_a_bomb()
    {
        var r = Bombed(amsuOn: true, from: 20, to: 30);   // my 1조 grows evenly from 10 to 30 s: half of it under the bomb
        Assert.Equal(0.5, Timeline.BombShare(r, new TimeWindow(0, 40)), 6);
        Assert.Equal(1, Timeline.BombShare(r, new TimeWindow(20, 30)), 6);
        Assert.Equal(0, Timeline.BombShare(r, new TimeWindow(30, 40)));   // no damage in it
    }

    [Fact]
    public void 암수_share_is_my_gain_over_all_my_damage()
    {
        Assert.Equal(0.05175, Timeline.AmsuGain(Bombed(amsuOn: true, from: 20, to: 30))!.Value.MyShare, 6);   // half of 1조 under it
        Assert.Equal(0, Timeline.AmsuGain(R(40) with { AmsuOn = true })!.Value.MyShare);                       // no damage at all
    }

    [Fact]
    public void Only_the_damage_inside_the_bomb_windows_counts()
    {
        var (mine, _, _) = Timeline.AmsuGain(Bombed(amsuOn: true, from: 20, to: 30))!.Value;   // half of the growth
        Assert.Equal(51_750_000_000, mine, 0);
    }

    [Fact]
    public void Without_암수_on_there_is_no_gain()
    {
        Assert.Null(Timeline.AmsuGain(Bombed(amsuOn: false)));
        Assert.Null(Timeline.AmsuGain(R(40)));   // a record from before 암수: no flag, no events
    }

    [Fact]
    public void Bomb_windows_run_to_the_end_when_no_end_came() =>
        Assert.Equal([(5.0, 40.0)], Timeline.AmsuSpans(R(40, null, E(5, "나", RecordEventKind.암수시작, 1))).Select(s => (s.From, s.To)));
}
