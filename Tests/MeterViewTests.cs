using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>What the meter window draws (MeterView.Build): my own figures (shown to me even when not sent) with the party's.</summary>
public class MeterViewTests
{
    const long Eok = 100_000_000;

    static MeterStatus Mine(long damage, long boss)
    {
        var s = MeterStatus.None;
        var panel = new JeonbunReading(damage, damage / 60, 60, false);
        foreach (var (hp, t) in new[] { (boss + 50 * Eok, 0), (50 * Eok, 1), (50 * Eok, 2), (50 * Eok, 3) })
            s = MeterTracker.Step(s, (hp, boss + 50 * Eok), panel with { Timer = 60 + t }, true, S(t));
        return s;
    }

    [Fact]
    public void Alone_my_own_figures_make_the_meter()
    {
        var v = MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.딜러, "나", 3, [], me: 0, host: 0, new MeterSettings(), new MeterHistory(), S(3));
        Assert.Equal(0.40, v.Rows.Single().Share!.Value, 2);
        Assert.Equal(0.60, v.Other!.Value, 2);
        Assert.True(v.PanelFound);
    }

    [Fact]
    public void In_a_party_my_local_figures_stand_in_for_mine_and_the_others_come_from_the_session()
    {
        MeterSample other = new(9, "민수", 0, MeterRole.딜러, 1, 30 * Eok, Eok, 63, true, 1, 100 * Eok);
        var v = MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.딜러, "나", 3, [other], me: 5, host: 9, new MeterSettings { Send = true }, new MeterHistory(), S(3));
        Assert.Equal(["나", "민수"], v.Rows.Select(r => r.Name));
        Assert.Equal(0.40, v.Rows[0].Share!.Value, 2);   // divided by the host's figure
    }

    [Fact]
    public void Every_pc_divides_by_the_same_boss_figure_when_the_host_does_not_send()
    {
        MeterSample P(ulong id, long boss) => new(id, $"p{id}", 0, MeterRole.딜러, 1, 20 * Eok, Eok, 63, true, 1, boss);
        MeterSample[] session = [P(1, 90 * Eok), P(2, 95 * Eok), P(3, 99 * Eok)];
        var onHost = MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.딜러, "나", 3, session, me: 9, host: 9, new MeterSettings { Send = true }, new MeterHistory(), S(3));
        var onMember = MeterView.Build(Mine(20 * Eok, 95 * Eok), MeterRole.딜러, "p1", 3, session, me: 1, host: 9, new MeterSettings { Send = true }, new MeterHistory(), S(3));
        Assert.Equal(95 * Eok, onHost.BossTaken);
        Assert.Equal(onHost.BossTaken, onMember.BossTaken);
    }

    [Fact]
    public void With_alignment_a_member_one_second_ahead_is_taken_at_the_common_second()
    {
        MeterSample P(ulong id, int timer, long damage) => new(id, $"p{id}", 0, MeterRole.딜러, 1, damage, Eok, timer, true, 1, 100 * Eok);
        var align = new MeterAlign();
        MeterView Step(params MeterSample[] session) =>
            MeterView.Build(MeterStatus.None, MeterRole.딜러, "나", 3, session, me: 9, host: 1, new MeterSettings { Scope = MeterScope.공대, Send = true }, new MeterHistory(), S(3), align: align);
        Step(P(1, 10, 30 * Eok), P(2, 10, 20 * Eok));
        var v = Step(P(1, 11, 33 * Eok), P(2, 10, 20 * Eok));
        Assert.Equal(0.30, v.Rows.Single(r => r.Id == 1).Share!.Value, 6);
    }

    [Fact]
    public void My_row_is_in_my_party_when_i_do_not_send()
    {
        var v = MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.딜러, "나", 3, [], me: 5, host: 9, new MeterSettings(), new MeterHistory(), S(3), myParty: 2);
        Assert.Equal(2, v.Rows.Single().Party);
    }

    [Fact]
    public void Without_the_panel_the_meter_says_so()
    {
        var v = MeterView.Build(MeterStatus.None, MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), new MeterHistory(), S(0));
        Assert.False(v.PanelFound);
        Assert.Empty(v.Rows);
    }

    [Fact]
    public void 기타_can_be_hidden() =>
        Assert.Null(MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings { Other = false }, new MeterHistory(), S(3)).Other);

    [Fact]
    public void Not_sending_means_not_receiving()
    {
        MeterSample other = new(9, "민수", 0, MeterRole.딜러, 1, 30 * Eok, Eok, 63, true, 1, 100 * Eok);
        var v = MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.딜러, "나", 3, [other], me: 5, host: 9, new MeterSettings { Send = false }, new MeterHistory(), S(3));
        Assert.Equal(["나"], v.Rows.Select(r => r.Name));
    }

    static BuffState[] Buffs(bool 공증, bool 아덴, bool 초각스) =>
        [new(BuffKind.공증, 공증, null, null, false, S(0)), new(BuffKind.아덴피증, 아덴, null, null, false, S(0)),
         new(BuffKind.초각스피증, 초각스, null, null, false, S(0)), new(BuffKind.뎀증, false, null, null, false, S(0))];

    static MeterStatus At(long damage, int fight = 1) =>
        MeterStatus.None with { Panel = new(damage, 0, 10, false), PanelFound = true, Clock = FightClockState.None with { Fight = fight } };

    [Fact]
    public void The_header_names_the_boss_and_my_share()
    {
        var v = MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.딜러, "나", 3, [], me: 0, host: 0, new MeterSettings(), new MeterHistory(), S(3));
        Assert.Equal(("고통의 마녀, 세르카", " · 내 40.0%"), (v with { BossName = "고통의 마녀, 세르카" }).Header(0, x => $"{x / Eok}억"));
        Assert.Equal(("보스", " · 내 40.0%"), v.Header(0, x => $"{x / Eok}억"));   // no name known
        Assert.Equal(("보스", " 100억"), (v with { Rows = [] }).Header(0, x => $"{x / Eok}억"));   // no share of mine: the boss damage
        Assert.Null((v with { Rows = [], BossTaken = 0 }).Header(0, x => $"{x / Eok}억"));   // nothing to show
    }

    [Fact]
    public void The_graph_follows_the_전분_timer_and_stops_while_it_is_stopped()
    {
        var h = new MeterHistory();
        var clock = FightClockState.None;
        void Second(int timer, double at)
        {
            clock = FightClock.Update(clock, timer, S(at));
            var mine = MeterStatus.None with { Panel = new(10 * Eok, Eok, timer, false), PanelFound = true, Clock = clock };
            MeterView.Build(mine, MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(at));
        }
        for (int t = 0; t <= 5; t++) Second(t, t);
        for (int at = 6; at <= 20; at++) Second(5, at);        // a phase change: the timer stopped for 15 s
        for (int t = 6; t <= 8; t++) Second(t, t + 15);
        var points = h.Series(0).Select(p => p.At.TotalSeconds).ToList();
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], points);   // one point per 전분 second, none for the stop
    }

    [Fact]
    public void Buff_coverage_is_the_share_of_my_damage_dealt_while_each_buff_was_on()
    {
        var h = new MeterHistory();
        MeterView V(long damage, BuffState[] b) => MeterView.Build(At(damage), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(1), buffs: b);
        V(0, Buffs(false, false, false));
        V(60 * Eok, Buffs(true, true, false));    // +60: 공증 and 아덴 on
        V(80 * Eok, Buffs(true, false, false));   // +20: 공증 only
        var v = V(100 * Eok, Buffs(false, false, true));   // +20: 초각스 only
        Assert.Equal([0.8, 0.6, 0.2], v.BuffCover!.Select(x => Math.Round(x, 6)));
    }

    static MeterStatus AtBoss(long damage, long bossTaken) => At(damage) with { Boss = BossDamageState.None with { Done = bossTaken } };

    [Fact]
    public void In_a_4_man_raid_each_buffs_party_share_is_the_boss_hp_lost_while_it_was_on()
    {
        var h = new MeterHistory();
        MeterView V(long damage, long boss, BuffState[] b, bool small = true) =>
            MeterView.Build(AtBoss(damage, boss), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(1), buffs: b, smallParty: small);
        V(0, 0, Buffs(false, false, false));
        V(60 * Eok, 100 * Eok, Buffs(true, true, false));    // the boss lost 100: 공증 and 아덴 on
        V(80 * Eok, 300 * Eok, Buffs(true, false, false));   // 200: 공증 only
        var v = V(100 * Eok, 400 * Eok + 999_999, Buffs(false, false, true));   // 100 (and under a million, not counted): 초각스 only
        Assert.Equal([0.75, 0.25, 0.25], v.BuffParty!.Select(x => Math.Round(x, 6)));
        Assert.Equal([0.8, 0.6, 0.2], v.BuffCover!.Select(x => Math.Round(x, 6)));   // mine as before
        Assert.Null(V(110 * Eok, 500 * Eok, Buffs(true, false, false), small: false).BuffParty);   // 8-man: the boss mixes two parties
    }

    // Left out of 버프 적용률 (LeftOut; user, 2026-11-11): the boss bar unread, or my skill bar tinted and 5 s after.
    static MeterView Bar(MeterHistory h, double at, double hpAt, long damage, long boss, BuffState[] b, bool gimmick = false,
        double tintAt = 0, MeterRole role = MeterRole.딜러) =>
        MeterView.Build(AtBoss(damage, boss) with { HpAt = S(hpAt), TintAt = S(tintAt) }, role, "나", 3, [], 0, 0, new MeterSettings(), h, S(at), buffs: b, smallParty: true, gimmick: gimmick);

    [Fact]
    public void A_second_the_boss_bar_is_unread_adds_nothing_buffed_or_not()
    {
        var h = new MeterHistory();
        Bar(h, 1, 1, 0, 0, Buffs(false, false, false));
        Bar(h, 2, 2, 100 * Eok, 100 * Eok, Buffs(true, false, false));    // +100 under 공증
        Bar(h, 4, 2, 200 * Eok, 100 * Eok, Buffs(true, true, true));      // the bar unread: +100 left out, whatever the buffs
        var v = Bar(h, 5, 5, 300 * Eok, 400 * Eok, Buffs(false, false, false));   // back: +100 unbuffed counts
        Assert.Equal([0.5, 0.0, 0.0], v.BuffCover!.Select(x => Math.Round(x, 6)));
        Assert.Equal([1.0, 0.0, 0.0], v.BuffParty!.Select(x => Math.Round(x, 6)));   // the 300 lost meanwhile came in one jump: not counted
        Bar(h, 6, 6, 300 * Eok, 400 * Eok, Buffs(false, false, false));
        v = Bar(h, 7, 7, 300 * Eok, 500 * Eok, Buffs(false, false, false));          // settled: the boss figure counts again
        Assert.Equal([0.5, 0.0, 0.0], v.BuffParty!.Select(x => Math.Round(x, 6)));
    }

    [Theory]
    [InlineData(MeterRole.딜러, 0.5)]      // dead at 3 s: that second and 5 s after are left out
    [InlineData(MeterRole.서포터, 0.75)]   // the dead time of a support stays in
    public void A_tinted_skill_bar_and_five_seconds_after_it_are_left_out_for_a_dealer_only(MeterRole role, double atk)
    {
        var h = new MeterHistory();
        Bar(h, 1, 1, 0, 0, Buffs(false, false, false), role: role);
        Bar(h, 2, 2, 100 * Eok, 0, Buffs(true, false, false), role: role);
        Bar(h, 3, 3, 200 * Eok, 0, Buffs(true, false, false), tintAt: 3, role: role);
        Bar(h, 8, 8, 300 * Eok, 0, Buffs(true, false, false), tintAt: 3, role: role);    // 5 s after the tint
        Bar(h, 9, 9, 400 * Eok, 0, Buffs(false, false, false), tintAt: 3, role: role);
        Assert.Equal(atk, h.Covered![0], 6);
    }

    [Fact]
    public void What_is_counted_first_after_a_hidden_hud_is_left_out_too()
    {
        var h = new MeterHistory();
        Bar(h, 1, 1, 0, 0, Buffs(false, false, false));
        Bar(h, 2, 2, 100 * Eok, 0, Buffs(true, false, false));
        for (int t = 4; t <= 6; t++)   // the whole HUD hidden: no panel, no bar
            MeterView.Build(MeterStatus.None with { HpAt = S(2), Clock = FightClockState.None with { Fight = 1 } }, MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(t));
        Bar(h, 7, 7, 300 * Eok, 0, Buffs(false, false, false));           // +200 dealt across it: left out
        var v = Bar(h, 8, 8, 300 * Eok, 0, Buffs(false, false, false));
        Assert.Equal([1.0, 0.0, 0.0], v.BuffCover!.Select(x => Math.Round(x, 6)));
    }

    [Fact]
    public void A_bar_never_read_or_대난투_in_its_place_is_not_an_unread_bar()
    {
        var never = new MeterHistory();
        Bar(never, 5, 0, 0, 0, Buffs(false, false, false));
        Bar(never, 6, 0, 100 * Eok, 0, Buffs(true, false, false));
        Assert.Equal([1.0, 0.0, 0.0], Bar(never, 7, 7, 100 * Eok, 100 * Eok, Buffs(false, false, false)).BuffCover!);

        var gimmick = new MeterHistory();
        Bar(gimmick, 1, 1, 0, 0, Buffs(false, false, false));
        Bar(gimmick, 4, 1, 100 * Eok, 0, Buffs(true, false, false), gimmick: true);
        Assert.Equal([1.0, 0.0, 0.0], Bar(gimmick, 5, 5, 100 * Eok, 100 * Eok, Buffs(false, false, false)).BuffCover!);
    }

    [Fact]
    public void No_party_share_before_the_boss_hp_is_read()
    {
        var h = new MeterHistory();
        MeterView.Build(At(0), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(1), buffs: Buffs(true, false, false), smallParty: true);
        var v = MeterView.Build(At(50 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(2), buffs: Buffs(true, false, false), smallParty: true);
        Assert.Null(v.BuffParty);
    }

    [Fact]
    public void Buff_coverage_starts_over_with_a_new_record_and_is_absent_without_buff_reads()
    {
        var h = new MeterHistory();
        MeterView.Build(At(50 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(1), buffs: Buffs(true, true, true));
        var v = MeterView.Build(At(10 * Eok, fight: 2), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(2), buffs: Buffs(false, false, false));
        Assert.Equal([0.0, 0.0, 0.0], v.BuffCover!);
        Assert.Null(MeterView.Build(At(50 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), new MeterHistory(), S(1), buffs: []).BuffCover);
    }

    [Fact]
    public void The_line_graph_has_the_dealers_and_always_me()
    {
        MeterSample P(ulong id, MeterRole role) => new(id, $"p{id}", 0, role, 1, 30 * Eok, Eok, 63, true, 1, 100 * Eok);
        var v = MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.서포터, "나", 3, [P(1, MeterRole.딜러), P(2, MeterRole.서포터)], me: 5, host: 1,
            new MeterSettings { Send = true }, new MeterHistory(), S(3));
        Assert.Equal([1UL, 5UL], v.Lines(me: 5, max: 4).Select(r => r.Id).Order());
    }

    [Fact]
    public void When_the_panel_is_lost_the_last_figures_and_graph_stay()
    {
        var h = new MeterHistory();
        var mine = Mine(40 * Eok, 100 * Eok);
        MeterView.Build(mine, MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(3));
        var lost = MeterView.Build(mine with { Panel = null, PanelFound = false }, MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(9));
        Assert.False(lost.PanelFound);
        Assert.Equal(0.40, lost.Rows.Single().Share!.Value, 2);
        Assert.Single(h.Series(0));   // nothing new added while it is lost
    }

    [Fact]
    public void As_a_support_the_buff_bars_are_not_offered()
    {
        var v = MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.서포터, "나", 3, [], 0, 0, new MeterSettings(), new MeterHistory(), S(3), buffs: [new(BuffKind.공증, true, null, null, false, S(0))]);
        Assert.False(v.ShowBuffBars);
        Assert.True(MeterView.Build(Mine(40 * Eok, 100 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), new MeterHistory(), S(3)).ShowBuffBars);
    }

    [Fact]
    public void A_buff_on_for_half_a_second_takes_half_of_that_seconds_damage()
    {
        var h = new MeterHistory();
        BuffState[] B(bool on, double changed) => [new(BuffKind.공증, on, null, null, false, S(changed)), new(BuffKind.아덴피증, false, null, null, false, S(0)),
            new(BuffKind.초각스피증, false, null, null, false, S(0)), new(BuffKind.뎀증, false, null, null, false, S(0))];
        MeterView.Build(At(0), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(10), buffs: B(false, 0));
        var v = MeterView.Build(At(100 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(11), buffs: B(true, 10.5));   // on at 10.5
        Assert.Equal(0.5, v.BuffCover![0], 6);
        v = MeterView.Build(At(200 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(12), buffs: B(false, 11.25));   // off at 11.25
        Assert.Equal((50 + 25) / 200.0, v.BuffCover![0], 6);
    }

    [Fact]
    public void A_support_sees_each_buff_split_between_the_dealers_never_itself()
    {
        var h = new MeterHistory();
        var settings = new MeterSettings { Send = true };
        BuffState[] B(bool 공증, bool 아덴) => [new(BuffKind.공증, 공증, null, null, false, S(0)), new(BuffKind.아덴피증, 아덴, null, null, false, S(0)),
            new(BuffKind.초각스피증, false, null, null, false, S(0)), new(BuffKind.뎀증, false, null, null, false, S(0))];
        MeterSample D(ulong id, string name, long damage, long? atk) => new(id, name, 0, MeterRole.딜러, 1, damage, 0, 10, true, 1, 0, atk);
        MeterView.Build(At(0), MeterRole.서포터, "나", 3, [D(2, "민수", 0, 0), D(3, "지현", 0, 0)], 1, 1, settings, h, S(10), buffs: B(false, false));
        var v = MeterView.Build(At(5 * Eok), MeterRole.서포터, "나", 3, [D(2, "민수", 60 * Eok, 60 * Eok), D(3, "지현", 40 * Eok, 20 * Eok)], 1, 1, settings, h, S(11), buffs: B(true, true));
        Assert.Equal([("민수", 0.75), ("지현", 0.25)], v.BuffSplit![0].Select(x => (x.Name, Math.Round(x.Share, 6))));   // 공증: as each sent it
        Assert.Equal([("민수", 0.6), ("지현", 0.4)], v.BuffSplit[1].Select(x => (x.Name, Math.Round(x.Share, 6))));     // 아덴: from my screen's timing
        Assert.Empty(v.BuffSplit[2]);                                                                                      // 초각스 never on
        Assert.True(v.ShowBuffBars);
    }

    [Fact]
    public void A_dealer_sees_the_buffs_part_of_their_own_damage_and_sends_their_공증_figure()
    {
        var h = new MeterHistory();
        var settings = new MeterSettings { Send = true };
        MeterSample D(ulong id, long damage) => new(id, $"p{id}", 0, MeterRole.딜러, 1, damage, 0, 10, true, 1, 0, 0);
        MeterView.Build(At(0), MeterRole.딜러, "나", 3, [D(2, 0), D(3, 0)], 1, 1, settings, h, S(10), buffs: Buffs(false, false, false));
        var v = MeterView.Build(At(60 * Eok), MeterRole.딜러, "나", 3, [D(2, 40 * Eok), D(3, 10 * Eok)], 1, 1, settings, h, S(11), buffs: Buffs(true, false, false));
        Assert.Null(v.BuffSplit);
        Assert.Equal(1.0, v.BuffCover![0], 6);
        Assert.Equal(60 * Eok, h.MyAtkBuffed);
    }

    [Fact]
    public void Alone_there_is_no_split_only_my_own_coverage() =>
        Assert.Null(MeterView.Build(At(10 * Eok), MeterRole.딜러, "나", 3, [], 1, 1, new MeterSettings(), new MeterHistory(), S(1), buffs: Buffs(true, false, false)).BuffSplit);

    [Fact]
    public void Damage_dealt_before_the_first_reading_is_not_put_on_any_buff()
    {
        var h = new MeterHistory();
        MeterView.Build(At(50 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(1), buffs: Buffs(false, false, true));   // joined mid-fight
        var v = MeterView.Build(At(60 * Eok), MeterRole.딜러, "나", 3, [], 0, 0, new MeterSettings(), h, S(2), buffs: Buffs(false, false, false));
        Assert.Equal(0, v.BuffCover![2], 6);   // the 50억 before is nobody's guess
    }
}
