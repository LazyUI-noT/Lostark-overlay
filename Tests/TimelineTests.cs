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
