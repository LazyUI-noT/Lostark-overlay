using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>종막 EX 2-3: the boss HP shows ???, so it is estimated from the party's damage (spec: the user's 2-3 rules).</summary>
public sealed class HiddenHpTests
{
    const long G22 = 2_223_536_657_594, G23 = 2_001_182_991_834;   // EX 하드
    static readonly RaidGate Ex22 = new("final-ex-g2-2", "종막: 최후의 날", "EX 2-2관문", [], [620], Raid.없음, new Dictionary<string, long> { ["EX 하드"] = G22 });
    static readonly RaidGate Ex23 = new("final-ex-g2-3", "종막: 최후의 날", "EX 2-3관문", [], [75, 40], Raid.없음,
        After: "final-ex-g2-2", HiddenHpByDifficulty: new Dictionary<string, long> { ["EX 하드"] = G23 });
    static readonly IReadOnlyList<RaidGate> Gates = [Ex22, Ex23];
    const long G21 = 550_000_000_000;   // the user's example: 2-1 HP 5500억
    static readonly RaidGate Ex21 = new("final-ex-g2-1", "종막: 최후의 날", "EX 2-1관문", [], [], Raid.없음, new Dictionary<string, long> { ["EX 하드"] = G21 });
    static readonly HiddenHpOptions Opt = new(43.5, 1.0, []);

    static MeterSample S(int party, MeterRole role, long damage, ulong id = 0) => new(id, "", 0, role, party, damage, 0, 100, true, 1, 0);
    static TimeSpan T(double s) => TimeSpan.FromSeconds(s);

    // 2-2's HP last read near 0 at 10 s; a running timer, fight 1.
    static MeterStatus Status(long last, TimeSpan hpAt, int fight = 1, bool frozen = false) =>
        MeterStatus.None with
        {
            Boss = new BossDamageState(0, G22, last, G22, last, null),
            Clock = FightClockState.None with { Fight = fight, Frozen = frozen, Timer = 100 },
            HpAt = hpAt,
        };

    [Fact]
    public void A_party_is_its_three_dealers_or_else_its_supports_assist_over_the_share()
    {
        Assert.Equal(600, HiddenHp.PartyDamage([S(1, MeterRole.딜러, 100), S(1, MeterRole.딜러, 200), S(1, MeterRole.딜러, 300), S(1, MeterRole.서포터, 87)], 43.5));
        Assert.Equal(200, HiddenHp.PartyDamage([S(1, MeterRole.딜러, 100), S(1, MeterRole.서포터, 87)], 43.5));   // 87 × 100 ÷ 43.5
        Assert.Null(HiddenHp.PartyDamage([S(1, MeterRole.딜러, 100)], 43.5));
    }

    [Fact]
    public void The_raid_is_both_parties_or_the_known_one_times_the_users_multiple()
    {
        IReadOnlyList<MeterSample> both = [S(1, MeterRole.서포터, 87), S(2, MeterRole.서포터, 174)];
        Assert.Equal(600, HiddenHp.RaidDamage(both, 1, 43.5, 1.0));
        Assert.Equal(300, HiddenHp.RaidDamage([S(1, MeterRole.서포터, 87)], 1, 43.5, 0.5));   // 200 + 200 × 0.5
        Assert.Equal(400, HiddenHp.RaidDamage([S(2, MeterRole.서포터, 87)], 1, 43.5, 1.0));   // only the other party's support sends
        Assert.Null(HiddenHp.RaidDamage([], 1, 43.5, 1.0));
    }

    [Fact]
    public void Two_three_starts_when_two_two_was_near_zero_and_its_hp_stopped_reading_with_the_timer_running()
    {
        Assert.Null(HiddenHp.Step(null, Gates, Status(G22 / 2, T(10)), 0, Opt, T(14)));        // 2-2 still far from 0
        Assert.Null(HiddenHp.Step(null, Gates, Status(1000, T(10)), 0, Opt, T(11)));          // HP read a second ago
        Assert.Null(HiddenHp.Step(null, Gates, Status(1000, T(10), frozen: true), 0, Opt, T(14)));
        var s = HiddenHp.Step(null, Gates, Status(1000, T(10)), 5000, Opt, T(14))!;
        Assert.Equal(("final-ex-g2-3", "EX 하드", G23, G23), (s.Gate.Id, s.Difficulty, s.Max, s.Left));
    }

    [Fact]
    public void The_estimate_takes_damage_since_the_start_the_esther_once_and_the_gimmick_drop()
    {
        var status = Status(1000, T(10));
        var opt = Opt with { Esthers = [new("공아만 3칸", 1000, 50)] };   // 1000억 at 50%
        var s = HiddenHp.Step(null, Gates, status, 1_000_000, opt, T(14))!;   // the base: damage dealt before 2-3
        s = HiddenHp.Step(s, Gates, status, 1_000_000 + G23 / 5, opt, T(20))!;
        Assert.Equal(G23 - G23 / 5, s.Left);                                   // 80%
        s = HiddenHp.Step(s, Gates, status, 1_000_000 + G23 / 2, opt, T(30))!;
        Assert.Equal(G23 - G23 / 2 - 100_000_000_000, s.Left);                 // 50%: the esther goes in
        long at25 = 1_000_000 + G23 * 3 / 4;
        s = HiddenHp.Step(s, Gates, status, at25, opt, T(40))!;                // past 25%: waiting for the gimmick
        Assert.Equal((GimmickPhase.Waiting, G23 - (at25 - 1_000_000) - 100_000_000_000), (s.Phase, s.Left));
        s = HiddenHp.Step(s, Gates, status, at25, opt, T(43))!;
        Assert.Equal(GimmickPhase.Waiting, s.Phase);                           // standing still, not yet 5 s
        s = HiddenHp.Step(s, Gates, status, at25, opt, T(45))!;
        Assert.Equal(GimmickPhase.Paused, s.Phase);                            // everyone down: the damage stands still
        s = HiddenHp.Step(s, Gates, status, at25 + 1000, opt, T(80))!;         // damage again after the revive: from 10%
        Assert.Equal((GimmickPhase.Done, (long)(G23 * 0.10) - 1000), (s.Phase, s.Left));
    }

    [Fact]
    public void Several_uses_go_in_at_their_own_percent_and_one_at_ten_comes_off_the_ten_after_the_gimmick_even_below_zero()
    {
        var status = Status(1000, T(10));
        var opt = Opt with { Esthers = [new("공아만 3칸", 1000, 60), new("히든 아만 3칸", 3000, 10)] };
        var s = HiddenHp.Step(null, Gates, status, 0, opt, T(14))!;
        s = HiddenHp.Step(s, Gates, status, G23 * 41 / 100, opt, T(20))!;                  // 59%: the first goes in
        Assert.Equal(G23 - G23 * 41 / 100 - 100_000_000_000, s.Left);
        s = HiddenHp.Step(s, Gates, status, G23 * 85 / 100, opt, T(30))!;                  // under 10% before the gimmick: 히든 아만 waits
        Assert.Equal(G23 - G23 * 85 / 100 - 100_000_000_000, s.Left);
        s = HiddenHp.Step(s, Gates, status, G23 * 85 / 100, opt, T(36))!;                  // paused
        s = HiddenHp.Step(s, Gates, status, G23 * 85 / 100 + 1, opt, T(60))!;              // 10% − 3000억: under 0 when 히든 아만 is bigger
        Assert.Equal((long)(G23 * 0.10) - 300_000_000_000 - 1, s.Left);
        Assert.True(s.Left < 0);
    }

    [Fact]
    public void The_plans_damage_is_an_hp_share_written_at_nightmare()
    {
        // 2-3 at EX 하드 has half the EX 나이트메어 HP here: a 1000억 use (나이트메어) takes 500억 off.
        var ex23 = Ex23 with { HiddenHpByDifficulty = new Dictionary<string, long> { ["EX 하드"] = G23, ["EX 나이트메어"] = G23 * 2 } };
        var gates = new[] { Ex22, ex23 };
        var opt = Opt with { Esthers = [new("공아만 3칸", 1000, 90)] };
        var s = HiddenHp.Step(null, gates, Status(1000, T(10)), 0, opt, T(14))!;
        s = HiddenHp.Step(s, gates, Status(1000, T(10)), G23 / 5, opt, T(20))!;
        Assert.Equal(G23 - G23 / 5 - 50_000_000_000, s.Left);
    }

    [Fact]
    public void Unknown_damage_keeps_the_last_estimate_and_a_new_record_or_a_readable_hp_ends_it()
    {
        var s = HiddenHp.Step(null, Gates, Status(1000, T(10)), 0, Opt, T(14))!;
        s = HiddenHp.Step(s, Gates, Status(1000, T(10)), 100, Opt, T(15))!;
        Assert.Equal(G23 - 100, HiddenHp.Step(s, Gates, Status(1000, T(10)), null, Opt, T(16))!.Left);
        Assert.Null(HiddenHp.Step(s, Gates, Status(1000, T(10), fight: 2), 100, Opt, T(16)));
        Assert.Null(HiddenHp.Step(s, Gates, Status(1000, T(16)), 100, Opt, T(16)));   // a bar read again: another boss
    }

    [Fact]
    public void Two_one_results_give_the_other_partys_ratio()
    {
        // Only party 1's support sends (87 → 200); 2-1 ended 1000억 : 1100억, so party 2 ≈ 200 × 1.1.
        Assert.Equal(420, HiddenHp.RaidDamage([S(1, MeterRole.서포터, 87)], 1, 43.5, 1.0, 1000, 1100));
        Assert.Equal(764, HiddenHp.RaidDamage([S(2, MeterRole.서포터, 174)], 1, 43.5, 1.0, 1000, 1100));   // only party 2 known: 400 + 400 × 1000/1100
        Assert.Equal(382, HiddenHp.RaidDamage([S(2, MeterRole.서포터, 87)], 2, 43.5, 1.0, 1000, 1100));    // party 2 is mine: 200 + 200 × 1000/1100
    }

    [Fact]
    public void Two_one_results_estimate_the_esther_when_the_user_gave_none()
    {
        // 2100억 × 100 ÷ 43.4 ≈ 4838.7억 by the players, so the 에스더 did 5500 − 4838.7 ≈ 661억: 12.0% of 2-1's HP.
        double share = HiddenHp.EstherShare(G21, 1000, 1100, 43.4)!.Value;
        Assert.Equal(0.1202, share, 3);
        var gates = new[] { Ex21, Ex22, Ex23 with { CalibrateFrom = "final-ex-g2-1" } };
        var o = new HiddenHpOptions(43.4, 1.0, [], 1000, 1100);   // no plan: 2-1's share at 50%
        var status = Status(1000, T(10));
        var s = HiddenHp.Step(null, gates, status, 0, o, T(14))!;
        s = HiddenHp.Step(s, gates, status, G23 / 2, o, T(20))!;
        long expected = G23 / 2 - (long)Math.Round(share * G23);
        Assert.InRange(s.Left, expected - 1_000_000, expected + 1_000_000);
        Assert.Null(HiddenHp.EstherShare(G21, 0, 1100, 43.4));   // a party unknown: nothing to work out
    }

    [Fact]
    public void Two_one_cleared_takes_each_sending_supports_assist()
    {
        var gates = new[] { Ex21, Ex22, Ex23 with { CalibrateFrom = "final-ex-g2-1" } };
        MeterStatus At(long last) => MeterStatus.None with { Boss = new BossDamageState(0, G21, last, G21, last, null) };
        IReadOnlyList<MeterSample> all = [S(1, MeterRole.서포터, 100_000_000_000), S(1, MeterRole.딜러, 5), S(2, MeterRole.딜러, 7)];
        Assert.Equal([("final-ex-g2-3", 1, 1000.0)], HiddenHp.Calibration(gates, At(0), all));
        Assert.Empty(HiddenHp.Calibration(gates, At(G21 / 3), all));   // 2-1 not cleared yet
    }

    [Fact]
    public void Two_ones_esther_plan_gives_the_share_two_one_shows()
    {
        // EX 나이트메어 2-1 (111,232억), both supports 24,253억, 2-1's 에스더 11,800억: the players did 99,432억, so 48.78%.
        Assert.Equal(48.78, HiddenHp.CalibratedShare(11_123_200_336_247, 24253, 24253, 11800)!.Value, 2);
        Assert.Null(HiddenHp.CalibratedShare(11_123_200_336_247, 24253, 0, 11800));
        Assert.Null(HiddenHp.CalibratedShare(11_123_200_336_247, 24253, 24253, 0));
    }

    [Fact]
    public void The_share_is_two_ones_with_its_esther_plan_else_the_users()
    {
        var gates = new[] { Ex21, Ex22, Ex23 with { CalibrateFrom = "final-ex-g2-1" } };
        var r = new RaidSettings { AssistShare = 43.5 };
        Assert.Equal(43.5, HiddenHp.Share(gates, gates[2], "EX 하드", r));                      // nothing entered
        (r.Gate("final-ex-g2-3").Assist1, r.Gate("final-ex-g2-3").Assist2) = (1200, 1200);
        Assert.Equal(43.5, HiddenHp.Share(gates, gates[2], "EX 하드", r));                      // no 2-1 plan yet
        r.Gate("final-ex-g2-1").Esthers = [new("카단 3칸", 0.5, 0)];                              // 5500억 − 0.5억 → 2400 × 100 ÷ 5499.5
        Assert.Equal(43.64, HiddenHp.Share(gates, gates[2], "EX 하드", r), 2);
        Assert.Equal(43.5, HiddenHp.Share(gates, gates[2], null, r));                           // difficulty unknown
    }
}
