using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>종막 2-3 (EX and 하드): the boss HP shows ???, so its HP removed is my damage ÷ my share of 2-2 (the user, 2026-11-12;
/// hidden-hp-own-damage.plan.md).</summary>
public sealed class HiddenHpTests
{
    const long G22 = 2_223_536_657_594;   // EX 하드 2-2
    static readonly RaidGate Ex22 = new("final-ex-g2-2", "종막: 최후의 날", "EX 2-2관문", [], [620], Raid.없음, new Dictionary<string, long> { ["EX 하드"] = G22 });
    static readonly RaidGate Ex23 = new("final-ex-g2-3", "종막: 최후의 날", "EX 2-3관문", [], [75, 40], Raid.없음, After: "final-ex-g2-2", Hidden: true);
    static readonly RaidGate Hard22 = new("final-g2-2", "종막: 최후의 날", "2-2관문 (하드)", [], [620], Raid.없음);
    static readonly RaidGate Hard23 = new("final-g2-3", "종막: 최후의 날", "2-3관문 (하드)", [], [75, 40], Raid.없음, After: "final-g2-2", Hidden: true);
    static readonly IReadOnlyList<RaidGate> Gates = [Ex22, Ex23, Hard22, Hard23];

    static TimeSpan T(double s) => TimeSpan.FromSeconds(s);

    /// <summary>A second of the meter: the bar at <paramref name="max"/> with <paramref name="last"/> left (removed since the
    /// record first saw it at full), last read at <paramref name="hpAt"/>; my panel at <paramref name="mine"/> (null: closed).</summary>
    static MeterStatus Status(long max, long last, TimeSpan hpAt, long? mine, bool support = false, int fight = 1, bool frozen = false) =>
        MeterStatus.None with
        {
            Panel = mine is { } d ? new JeonbunReading(d, 0, 100, support) : null,
            Boss = new BossDamageState(0, max, last, max, last, null),
            Clock = FightClockState.None with { Fight = fight, Frozen = frozen, Timer = 100 },
            HpAt = hpAt,
        };

    // 2-2 seen from full: I dealt 1/10 of what it lost.
    static ShareTrack Seen22(long max = G22, long mine = 0, MeterRole role = MeterRole.딜러, bool support = false)
    {
        var t = HiddenHp.Track(null, Status(max, max, T(1), mine, support), role, T(1));
        return HiddenHp.Track(t, Status(max, max / 2, T(60), mine + max / 20, support), role, T(60))!;
    }

    [Fact]
    public void My_share_is_my_damage_over_the_hp_removed_since_both_first_read()
    {
        var t = HiddenHp.Track(null, Status(G22, G22 - 1000, T(1), 5_000), MeterRole.딜러, T(1));   // the app started mid-2-2
        t = HiddenHp.Track(t, Status(G22, G22 / 2 - 1000, T(60), 5_000 + G22 / 10), MeterRole.딜러, T(60));
        Assert.Equal(0.2, t!.Share!.Value, 6);   // (G22/10) ÷ (G22/2): what came before the first joint read is left out
    }

    [Fact]
    public void Under_a_fifth_of_two_two_seen_or_no_damage_of_mine_gives_no_share()
    {
        var t = HiddenHp.Track(null, Status(G22, G22, T(1), 0), MeterRole.딜러, T(1));
        Assert.Null(HiddenHp.Track(t, Status(G22, G22 - G22 / 10, T(30), G22 / 100), MeterRole.딜러, T(30))!.Share);   // 10% seen
        Assert.Null(HiddenHp.Track(t, Status(G22, G22 / 2, T(30), 0), MeterRole.딜러, T(30))!.Share);                 // nothing of mine
    }

    [Fact]
    public void The_share_freezes_while_the_bar_isnt_read_so_two_threes_damage_stays_out_of_it()
    {
        var t = Seen22();
        var later = HiddenHp.Track(t, Status(G22, G22 / 2, T(60), G22), MeterRole.딜러, T(70));   // bar last read 10 s ago
        Assert.Equal(t.Share, later!.Share);
    }

    [Fact]
    public void A_new_record_or_another_bar_starts_the_share_over()
    {
        var t = Seen22();
        Assert.Null(HiddenHp.Track(t, Status(G22, G22, T(61), 0, fight: 2), MeterRole.딜러, T(61))!.Share);
        Assert.Null(HiddenHp.Track(t, Status(G22 * 2, G22 * 2, T(61), G22), MeterRole.딜러, T(61))!.Share);
    }

    [Fact]
    public void The_damage_column_must_match_my_role()
    {
        Assert.True(HiddenHp.ColumnFits(MeterRole.딜러, new JeonbunReading(1, 1, 1, false)));
        Assert.True(HiddenHp.ColumnFits(MeterRole.서포터, new JeonbunReading(1, 1, 1, true)));
        Assert.False(HiddenHp.ColumnFits(MeterRole.서포터, new JeonbunReading(1, 1, 1, false)));   // a support reading 피해량
        Assert.False(HiddenHp.ColumnFits(MeterRole.딜러, new JeonbunReading(1, 1, 1, true)));
        Assert.Null(HiddenHp.Track(null, Status(G22, G22, T(1), 0), MeterRole.서포터, T(1)));      // no share from the wrong number
        Assert.NotNull(Seen22(role: MeterRole.서포터, support: true).Share);                       // 조력 피해량: works the same
    }

    [Fact]
    public void Two_three_starts_after_two_two_near_zero_once_its_bar_stops_reading_with_the_timer_running()
    {
        var t = Seen22();
        Assert.Null(HiddenHp.Step(null, Gates, null, Status(G22, G22 / 2, T(10), 0), t, MeterRole.딜러, T(14)));             // 2-2 still far from 0
        Assert.Null(HiddenHp.Step(null, Gates, null, Status(G22, 1000, T(10), 0), t, MeterRole.딜러, T(11)));                // read a second ago
        Assert.Null(HiddenHp.Step(null, Gates, null, Status(G22, 1000, T(10), 0, frozen: true), t, MeterRole.딜러, T(14)));
        var s = HiddenHp.Step(null, Gates, null, Status(G22, 1000, T(10), 0), t, MeterRole.딜러, T(14))!;
        Assert.Equal(("final-ex-g2-3", (long)Math.Round(G22 * 0.9), 0.1), (s.Gate.Id, s.Max, Math.Round(s.Share!.Value, 6)));
    }

    [Fact]
    public void Hard_two_two_with_no_hp_in_the_data_is_found_by_its_name()
    {
        const long H22 = 1_000_000_000_000;
        var t = Seen22(H22);
        Assert.Null(HiddenHp.Step(null, Gates, null, Status(H22, 1000, T(10), 0), t, MeterRole.딜러, T(14)));   // unknown HP, no name: nothing
        var s = HiddenHp.Step(null, Gates, Hard22, Status(H22, 1000, T(10), 0), t, MeterRole.딜러, T(14))!;
        Assert.Equal(("final-g2-3", 900_000_000_000L), (s.Gate.Id, s.Max));
    }

    [Fact]
    public void Hp_removed_is_my_damage_since_two_three_began_over_my_share()
    {
        var t = Seen22();   // share 0.1
        var s = HiddenHp.Step(null, Gates, null, Status(G22, 1000, T(10), 7_000), t, MeterRole.딜러, T(14))!;
        Assert.Equal((7_000L, 0L), (s.MyBase!.Value, s.Removed));
        s = HiddenHp.Step(s, Gates, null, Status(G22, 1000, T(10), 7_000 + 100_000_000_000), t, MeterRole.딜러, T(20))!;
        Assert.Equal((long)Math.Round(100_000_000_000 / s.Share!.Value), s.Removed);   // 1000억 of mine ÷ 0.1 (G22's odd half: ≈1조)
        Assert.InRange(s.Removed, 999_999_000_000, 1_000_001_000_000);
        Assert.Equal(s.Removed * 100.0 / s.Max, s.RemovedPercent, 6);
        Assert.Equal((long)Math.Round(s.Max * HiddenHp.Target) - s.Removed, s.ToTarget);
    }

    [Fact]
    public void A_new_record_or_a_bar_reading_again_ends_two_three()
    {
        var t = Seen22();
        var s = HiddenHp.Step(null, Gates, null, Status(G22, 1000, T(10), 0), t, MeterRole.딜러, T(14))!;
        Assert.Null(HiddenHp.Step(s, Gates, null, Status(G22, 1000, T(10), 0, fight: 2), t, MeterRole.딜러, T(15)));
        Assert.Null(HiddenHp.Step(s, Gates, null, Status(G22, 1000, T(15), 0), t, MeterRole.딜러, T(15)));
    }

    [Fact]
    public void The_window_says_why_it_cant_estimate()
    {
        var t = Seen22();
        var s = HiddenHp.Step(null, Gates, null, Status(G22, 1000, T(10), 0), t, MeterRole.딜러, T(14))!;
        Assert.Equal("≈? 전분 필요", HiddenHp.Label(s, null, MeterRole.딜러));
        Assert.Equal("≈? 조력 피해량 열 필요", HiddenHp.Label(s, new JeonbunReading(1, 1, 1, false), MeterRole.서포터));
        Assert.Equal("≈? 피해량 열 필요", HiddenHp.Label(s, new JeonbunReading(1, 1, 1, true), MeterRole.딜러));
        Assert.Equal("≈? 2-2 기록 없음", HiddenHp.Label(s with { Share = null }, new JeonbunReading(1, 1, 1, false), MeterRole.딜러));
    }

    [Fact]
    public void The_window_shows_removed_over_the_target_and_whats_left_to_it_or_past_it()
    {
        var panel = new JeonbunReading(1, 1, 1, false);
        var s = new HiddenHpState(Ex23, 1_000_000_000_000, 1, T(0), 0.1, 0, 450_000_000_000);
        Assert.Equal("≈45% / 70% · 2500억", HiddenHp.Label(s, panel, MeterRole.딜러));
        Assert.Equal("≈78% / 70% · +800억", HiddenHp.Label(s with { Removed = 780_000_000_000 }, panel, MeterRole.딜러));
        Assert.Equal("≈10% / 70% · 3.00조", HiddenHp.Label(new HiddenHpState(Ex23, 5_000_000_000_000, 1, T(0), 0.1, 0, 500_000_000_000), panel, MeterRole.딜러));
        Assert.Equal(55, s.LeftPercent, 6);   // the alerts stay on HP % left
    }
    [Fact]
    public void Two_three_counts_only_the_column_of_my_role_even_when_it_flips()
    {
        var t = Seen22();   // a dealer, share 0.1
        var s = HiddenHp.Step(null, Gates, null, Status(G22, 1000, T(10), 900_000, support: true), t, MeterRole.딜러, T(14))!;
        Assert.Null(s.MyBase);   // the 조력 column gives no base for a dealer
        s = HiddenHp.Step(s, Gates, null, Status(G22, 1000, T(10), 7_000), t, MeterRole.딜러, T(15))!;
        Assert.Equal(7_000L, s.MyBase);
        s = HiddenHp.Step(s, Gates, null, Status(G22, 1000, T(10), 50_000_000_000, support: true), t, MeterRole.딜러, T(16))!;
        Assert.Equal((7_000L, 0L), (s.MyBase!.Value, s.Removed));   // flipped to 조력: held, not counted
    }

    [Fact]
    public void Hp_removed_never_goes_back_when_the_panel_misreads_lower()
    {
        var t = Seen22();
        var s = HiddenHp.Step(null, Gates, null, Status(G22, 1000, T(10), 0), t, MeterRole.딜러, T(14))!;
        s = HiddenHp.Step(s, Gates, null, Status(G22, 1000, T(10), 10_000_000_000), t, MeterRole.딜러, T(20))!;
        long high = s.Removed;
        s = HiddenHp.Step(s, Gates, null, Status(G22, 1000, T(10), 1_000_000_000), t, MeterRole.딜러, T(21))!;
        Assert.Equal(high, s.Removed);
    }

    [Fact]
    public void A_share_under_one_percent_or_over_all_of_it_is_no_share()
    {
        var t = HiddenHp.Track(null, Status(G22, G22, T(1), 0), MeterRole.딜러, T(1));
        Assert.Null(HiddenHp.Track(t, Status(G22, G22 / 2, T(60), G22 / 1000), MeterRole.딜러, T(60))!.Share);   // 0.2%: a misread
        Assert.Null(HiddenHp.Track(t, Status(G22, G22 / 2, T(60), G22), MeterRole.딜러, T(60))!.Share);          // 200%: impossible
        Assert.NotNull(HiddenHp.Track(t, Status(G22, G22 / 2, T(60), G22 / 2), MeterRole.딜러, T(60))!.Share);   // 100%: solo
    }
}
