using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>What the buff lock leaves on screen (buff-lock.design.md): 공증 only, capped at 85%.</summary>
public class BuffLockTests
{
    [Theory]
    [InlineData(0.62, false, "62%")]
    [InlineData(0.97, false, "97%")]
    [InlineData(0.62, true, "62%")]
    [InlineData(0.843, true, "84%")]
    [InlineData(0.846, true, "85+%")]   // would read 85%: capped
    [InlineData(0.97, true, "85+%")]
    [InlineData(1.4, false, "100%")]
    [InlineData(-0.2, true, "0%")]
    public void Figure_caps_at_85_when_locked(double share, bool locked, string expected) =>
        Assert.Equal(expected, BuffLock.Figure(share, locked ? 0.85 : null));

    [Theory]
    [InlineData(0.79, 0.80, "79%")]
    [InlineData(0.80, 0.80, "80+%")]
    [InlineData(0.89, 0.90, "89%")]
    [InlineData(0.97, 0.90, "90+%")]
    public void Figure_caps_at_the_fights_cap(double share, double cap, string expected) =>
        Assert.Equal(expected, BuffLock.Figure(share, cap));

    [Theory]
    [InlineData(0.97, false, 0.97)]
    [InlineData(0.97, true, 0.85)]
    [InlineData(0.5, true, 0.5)]
    [InlineData(1.5, false, 1.0)]
    public void Bar_stops_at_85_when_locked(double share, bool locked, double expected) =>
        Assert.Equal(expected, BuffLock.Bar(share, locked ? 0.85 : null), 6);

    [Fact]
    public void Bar_stops_at_the_fights_cap() => Assert.Equal(0.9, BuffLock.Bar(0.97, 0.9), 6);

    static MeterView View() => new([], null, 0, null, false, true, new MeterHistory(),
        BuffCover: [0.9, 0.5, 0.4], BuffParty: [0.88, 0.6, 0.3],
        BuffSplit: [[(1UL, "가", 0.6), (2UL, "나", 0.4)], [(1UL, "가", 1.0)], [(2UL, "나", 1.0)]]);

    [Fact]
    public void Locked_meter_keeps_only_공증()
    {
        var v = BuffLock.Apply(View(), 0.9);
        Assert.True(v.BuffLocked);
        Assert.Equal(0.9, v.BuffCap);
        Assert.Equal([0.9], v.BuffCover!);
        Assert.Equal([0.88], v.BuffParty!);
        Assert.Single(v.BuffSplit!);
        Assert.Equal(2, v.BuffSplit![0].Count);
    }

    [Fact]
    public void Unlocked_meter_is_untouched()
    {
        var before = View();
        Assert.Same(before, BuffLock.Apply(before, null));
    }

    [Fact]
    public void Locked_meter_with_nothing_read_stays_empty()
    {
        var v = BuffLock.Apply(new MeterView([], null, 0, null, false, true, new MeterHistory()), 0.85);
        Assert.Null(v.BuffCover);
        Assert.Null(v.BuffParty);
        Assert.Null(v.BuffSplit);
    }

    [Fact]
    public void Locked_timeline_has_no_초각스_lane()
    {
        Assert.Equal([BuffKind.공증, BuffKind.아덴피증, BuffKind.초각스피증], BuffLock.TimelineKinds(false));
        Assert.Equal([BuffKind.공증, BuffKind.아덴피증], BuffLock.TimelineKinds(true));
    }

    static FightRecord Fight(DateTime started, double seconds) => new(started, null, seconds, [], "나", 0, null, [], [], [], []);

    [Fact]
    public void Locked_아덴_피증_shows_10_minutes_after_the_fight_ends()
    {
        var start = new DateTime(2026, 11, 12, 21, 0, 0);
        var r = Fight(start, 300);   // ends 21:05
        Assert.False(BuffLock.LineShown(BuffKind.아덴피증, true, r, start.AddMinutes(14.9)));
        Assert.True(BuffLock.LineShown(BuffKind.아덴피증, true, r, start.AddMinutes(15)));
        Assert.True(BuffLock.LineShown(BuffKind.아덴피증, false, r, start.AddMinutes(6)));
        Assert.True(BuffLock.LineShown(BuffKind.공증, true, r, start.AddMinutes(6)));
        Assert.False(BuffLock.LineShown(BuffKind.초각스피증, true, r, start.AddDays(3)));
    }

    [Fact]
    public void Locked_shows_a_percent_for_공증_only()
    {
        Assert.True(BuffLock.PercentShown(BuffKind.공증, true));
        Assert.False(BuffLock.PercentShown(BuffKind.아덴피증, true));
        Assert.False(BuffLock.PercentShown(BuffKind.초각스피증, true));
        Assert.All(Enum.GetValues<BuffKind>(), k => Assert.True(BuffLock.PercentShown(k, false)));
    }

    // The level bands and overrides (user, 2026-11-13): ≤1720 → 80, else 85; 세르카 · 벨가르딘 나이트메어 90; 종막 EX 나이트메어 none.
    static readonly BuffCapRules Rules = new(85, [new BuffBand(1720, 80)]);

    static RaidGate Gate(Dictionary<string, int> levels, Dictionary<string, int?>? caps = null) =>
        new("g", "레이드", "1관문", [], [], Raid.없음, LevelByDifficulty: levels, BuffCapByDifficulty: caps);

    static readonly RaidGate Serka = Gate(new() { ["노말"] = 1710, ["하드"] = 1730, ["나이트메어"] = 1740 }, new() { ["나이트메어"] = 90 });
    static readonly RaidGate FinalEx = Gate(new() { ["EX 노말"] = 1750, ["EX 하드"] = 1770, ["EX 나이트메어"] = 1780 }, new() { ["EX 나이트메어"] = null });

    [Theory]
    [InlineData("노말", 0.80)]
    [InlineData("싱글", 0.80)]   // 싱글 and 매칭: 노말's level
    [InlineData("매칭", 0.80)]
    [InlineData("하드", 0.85)]
    [InlineData("나이트메어", 0.90)]
    [InlineData(null, 0.85)]      // difficulty not read yet
    [InlineData("헬", 0.85)]      // a difficulty without a level
    public void Cap_follows_the_level_bands_and_overrides(string? difficulty, double expected) =>
        Assert.Equal(expected, BuffLock.CapFor(Serka, difficulty, Rules));

    [Fact]
    public void Final_EX_nightmare_has_no_lock() => Assert.Null(BuffLock.CapFor(FinalEx, "EX 나이트메어", Rules));

    [Fact]
    public void Final_EX_hard_falls_in_the_top_band() => Assert.Equal(0.85, BuffLock.CapFor(FinalEx, "EX 하드", Rules));

    [Fact]
    public void An_unknown_raid_uses_the_default() => Assert.Equal(0.85, BuffLock.CapFor(null, "노말", Rules));

    [Fact]
    public void A_level_on_the_band_edge_is_in_it() =>
        Assert.Equal(0.80, BuffLock.CapFor(Gate(new() { ["하드"] = 1720 }), "하드", Rules));

    [Fact]
    public void Shipped_raids_json_gives_the_users_caps()
    {
        var dir = Path.Combine(RepoRoot(), "Profiles");
        var gates = RaidData.Load(dir);
        var rules = RaidData.LoadBuffCaps(dir);
        double? Cap(string id, string d) => BuffLock.CapFor(RaidData.Find(gates, id), d, rules);
        Assert.Equal(0.80, Cap("act4-g1", "하드"));
        Assert.Equal(0.80, Cap("final-g1", "노말"));
        Assert.Equal(0.85, Cap("final-g1", "하드"));
        Assert.Equal(0.80, Cap("serka-g1", "노말"));
        Assert.Equal(0.85, Cap("serka-g2", "하드"));
        Assert.Equal(0.90, Cap("serka-g2", "나이트메어"));
        Assert.Equal(0.85, Cap("belgardin-g1", "노말"));
        Assert.Equal(0.90, Cap("belgardin-g2", "나이트메어"));
        Assert.Equal(0.80, Cap("belltower-g1", "2단계"));
        Assert.Equal(0.85, Cap("belltower-g2", "3단계"));
        Assert.Equal(0.85, Cap("final-ex-g2-1", "EX 하드"));
        Assert.Null(Cap("final-ex-g2-2", "EX 나이트메어"));
        Assert.Null(Cap("final-ex-g2-3", "EX 나이트메어"));
    }

    [Fact]
    public void The_boss_HP_names_the_gate_and_difficulty_first()
    {
        var serka = Serka with { HpByDifficulty = new Dictionary<string, long> { ["노말"] = 100, ["나이트메어"] = 900 } };
        Assert.Equal(0.90, BuffLock.CapFor([serka], Rules, 900, null, null));
        Assert.Equal(0.80, BuffLock.CapFor([serka], Rules, 100, FinalEx, "EX 나이트메어"));   // the HP wins over the name
        Assert.Null(BuffLock.CapFor([serka], Rules, 555, FinalEx, "EX 나이트메어"));          // no HP match: by name
        Assert.Equal(0.85, BuffLock.CapFor([serka], Rules, null, null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(150)]
    public void A_gate_cap_outside_1_to_100_falls_back_to_the_bands(int bad) =>
        Assert.Equal(0.85, BuffLock.CapFor(Gate(new() { ["하드"] = 1730 }, new() { ["하드"] = bad }), "하드", Rules));
}
