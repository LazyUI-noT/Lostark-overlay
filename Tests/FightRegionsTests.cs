using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The timeline's regions (2-1 / 2-2 / 2-3), from the max HP the bar showed; numbers from the 2026-11-04 종막 EX 하드 run.</summary>
public class FightRegionsTests
{
    const long Hp21 = 3_955_211_667_283, Hp22 = 2_223_536_657_594;
    static readonly DateTime Start = new(2026, 11, 4, 0, 49, 42);

    static readonly IReadOnlyList<RaidGate> Gates = RaidData.Load(Profiles());

    static string Profiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Profiles"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "Profiles");
    }

    static RecordBoss B(double t, long? max, long taken = 0) => new(t, taken, max, max);

    static FightRecord Fight(IReadOnlyList<RecordBoss> boss, IReadOnlyList<RecordSample> samples, double duration, DateTime? start = null, string? gate = "종막: 최후의 날 · EX 2-1관문", bool? cleared = true, string? character = "살살") =>
        new(start ?? Start, gate, duration, [], "나", 3, null, [], samples, boss, [], Cleared: cleared, Difficulty: "EX 하드", Character: character);

    static RecordSample Me(double t, long damage) => new(t, "나", damage, 0);

    static IEnumerable<RecordBoss> Reads(double from, double to, long? max) => Enumerable.Range((int)from, (int)(to - from) + 1).Select(t => B(t, max));

    [Fact]
    public void A_fight_on_one_bar_has_no_regions()
    {
        var r = Fight([.. Reads(4, 100, Hp21)], [Me(4, 0), Me(100, 1000)], 100);

        Assert.Empty(FightRegions.Of(r, Gates));
    }

    [Fact]
    public void The_regions_are_named_timed_and_counted_from_the_bars_max_hp()
    {
        const long Dead21 = 3_952_524_167_113, Dead22 = 6_176_060_824_706;
        var r = Fight([B(4, null), B(5, Hp21), B(527, Hp21, Dead21), B(530, Hp22, Dead21), B(531, Hp22, Dead21), B(532, Hp22, Dead21), B(824, 7, Dead22), B(825, 7, Dead22), B(826, 7, Dead22), B(975, 7, Dead22)],
            [Me(4, 0), Me(530, 524_705_000_000), Me(824, 833_634_000_000), Me(975, 998_119_000_000)], 975);

        var regions = FightRegions.Of(r, Gates);

        Assert.Equal(new[] { "2-1", "2-2", "2-3" }, regions.Select(x => x.Name));
        Assert.Equal(new[] { 4d, 530d, 824d }, regions.Select(x => x.From));
        Assert.Equal(new[] { 530d, 824d, 975d }, regions.Select(x => x.To));
        Assert.Equal(new[] { false, false, true }, regions.Select(x => x.Hidden));
        Assert.Equal(new long?[] { Dead21, Dead22 - Dead21, null }, regions.Select(x => x.BossTaken));
        Assert.Equal(308_929_000_000, regions[1].Players[0].Damage);
        Assert.Equal(308_929_000_000 / 294d, regions[1].Players[0].Dps, 1);
    }

    [Fact]
    public void A_gate_without_hp_data_is_named_after_the_one_before_it()
    {
        long hard21 = 1_298_579_187_963, hard22 = 900_000_000_000;
        var r = Fight([.. Reads(4, 50, hard21), .. Reads(51, 100, hard22), .. Reads(101, 150, 7)], [Me(4, 0), Me(150, 1000)], 150);

        Assert.Equal(new[] { "2-1", "2-2", "2-3" }, FightRegions.Of(r, Gates).Select(x => x.Name));
    }

    [Fact]
    public void Bars_no_gate_knows_are_numbered()
    {
        var r = Fight([.. Reads(4, 50, 500_000_000_000), .. Reads(51, 100, 300_000_000_000)], [Me(4, 0), Me(100, 1000)], 100, gate: null);

        Assert.Equal(new[] { "1구간", "2구간" }, FightRegions.Of(r, Gates).Select(x => x.Name));
    }

    [Fact]
    public void A_misread_or_a_wobble_of_the_max_is_no_region()
    {
        var r = Fight([.. Reads(4, 50, Hp21), B(51, 39_552_116), B(52, Hp21), B(53, Hp21 - 3), B(54, 7), B(55, Hp21), .. Reads(56, 100, Hp21)], [Me(4, 0), Me(100, 1000)], 100);

        Assert.Empty(FightRegions.Of(r, Gates));
    }

    [Fact]
    public void Each_player_gets_their_own_figures_me_first()
    {
        RecordSample Other(double t, long damage) => new(t, "남", damage, 0);
        var r = Fight([.. Reads(0, 10, Hp21), .. Reads(11, 20, Hp22)], [Me(0, 0), Other(0, 0), Me(11, 110), Other(11, 330), Me(20, 200), Other(20, 600)], 20);

        var second = FightRegions.Of(r, Gates)[1];

        Assert.Equal(new[] { "나", "남" }, second.Players.Select(p => p.Name));
        Assert.Equal(new[] { 90L, 270L }, second.Players.Select(p => p.Damage));
        Assert.Equal(10, second.Players[0].Dps, 3);
    }

    [Fact]
    public void A_fight_ending_on_a_hidden_bar_ended_hidden_only_after_a_real_bar_and_some_reads()
    {
        Assert.True(FightRegions.EndedHidden([.. Reads(4, 50, Hp22), .. Reads(51, 60, 7)]));
        Assert.False(FightRegions.EndedHidden([.. Reads(4, 50, Hp22), B(51, 7)]));   // one misread at a wipe
        Assert.False(FightRegions.EndedHidden([.. Reads(4, 50, 7)]));   // nothing real before it
        Assert.False(FightRegions.EndedHidden([.. Reads(4, 50, Hp22)]));
    }

    static readonly FightRecord Part1 = Fight([.. Reads(4, 527, Hp21)], [Me(4, 0), Me(527, 500)], 527);
    static readonly FightRecord Part2 = Fight([.. Reads(530, 975, Hp22)], [Me(530, 500), Me(975, 900)], 975, Start.AddSeconds(568), "종막: 최후의 날 · EX 2-2관문", false);

    [Fact]
    public void Files_whose_clock_and_damage_go_on_join_whichever_is_opened()
    {
        var other = Fight([.. Reads(4, 90, Hp21)], [Me(4, 0), Me(90, 50)], 90, Start.AddHours(1));

        foreach (var opened in new[] { Part1, Part2 })
            Assert.Equal(new[] { Part1.StartedAt, Part2.StartedAt }, FightRegions.Chain(opened, [Part2, other, Part1]).Select(x => x.StartedAt));
    }

    [Theory]
    [InlineData(0, 0, "살살")]        // a retry: the clock starts over
    [InlineData(530, 400, "살살")]    // the damage starts over
    [InlineData(530, 500, "밝키리")]  // another character
    public void Other_fights_do_not_join(int from, int damage, string character)
    {
        var next = Fight([.. Reads(from, from + 100, Hp22)], [Me(from, damage), Me(from + 100, damage + 100)], from + 100, Start.AddSeconds(568), character: character);

        Assert.Single(FightRegions.Chain(Part1, [Part1, next]));
    }

    [Fact]
    public void A_gate_part_and_the_part_after_it_save_as_the_whole_gate()
    {
        var part2 = Part2 with { Gate = "최후의 날", Difficulty = null };   // 2-2 has no HP in raids.json: named from the panel

        var merged = FightRegions.Continued(Part1, part2);

        Assert.NotNull(merged);
        Assert.Equal("종막: 최후의 날 · EX 2관문", merged.Gate);
        Assert.Equal("EX 하드", merged.Difficulty);
        Assert.Equal(Part1.StartedAt, merged.StartedAt);
        Assert.Equal(Part2.Duration, merged.Duration);
        Assert.False(merged.Cleared);   // the last part's result
        Assert.Equal(Part1.Samples.Count + Part2.Samples.Count, merged.Samples.Count);
    }

    [Fact]
    public void What_only_the_next_part_read_is_kept_in_the_merge()
    {
        var part2 = Part2 with { CombatPower = 4321, ItemLevel = 1720, MyClassName = "워로드", MySubclass = "고독한 기사" };

        var merged = FightRegions.Continued(Part1, part2)!;

        Assert.Equal((4321d, 1720d, "워로드", "고독한 기사"), (merged.CombatPower, merged.ItemLevel, merged.MyClassName, merged.MySubclass));
    }

    [Fact]
    public void A_whole_gate_takes_its_next_part_too()
    {
        var whole = FightRegions.Continued(Part1, Part2)!;
        var part3 = Fight([.. Reads(980, 1100, null)], [Me(980, 900), Me(1100, 1000)], 1100, Start.AddSeconds(1000), "종막: 최후의 날 · EX 2-3관문");

        Assert.Equal("종막: 최후의 날 · EX 2관문", FightRegions.Continued(whole, part3)?.Gate);
    }

    [Theory]
    [InlineData("종막: 최후의 날 · 1관문")]   // a gate with no parts
    [InlineData(null)]
    public void A_gate_without_parts_is_not_merged(string? gate)
    {
        Assert.Null(FightRegions.Continued(Part1 with { Gate = gate }, Part2));
    }

    [Fact]
    public void A_retry_of_a_part_is_not_merged()
    {
        var retry = Fight([.. Reads(4, 100, Hp21)], [Me(4, 0), Me(100, 50)], 100, Start.AddSeconds(600));

        Assert.Null(FightRegions.Continued(Part1, retry));
    }

    [Fact]
    public void A_fight_hours_later_on_the_same_numbers_does_not_join()
    {
        Assert.Single(FightRegions.Chain(Part1, [Part1, Part2 with { StartedAt = Start.AddHours(3) }]));
    }

    [Fact]
    public void A_joined_fight_runs_the_whole_clock_and_ends_as_its_last_part()
    {
        var joined = FightRegions.Join([Part1, Part2]);

        Assert.Equal(Start, joined.StartedAt);
        Assert.Equal(975, joined.Duration);
        Assert.False(joined.Cleared);
        Assert.Equal(Part1.Boss.Count + Part2.Boss.Count, joined.Boss.Count);
        Assert.Equal("종막: 최후의 날 · EX 2-1관문 ~ EX 2-2관문", joined.Gate);
        Assert.Equal(new[] { "2-1", "2-2" }, FightRegions.Of(joined, Gates).Select(x => x.Name));
    }

    [Fact]
    public void Boss_damage_that_restarts_in_the_next_file_keeps_counting_in_the_joined_run()
    {
        var first = Fight([B(5, Hp21), B(6, Hp21), B(7, Hp21), B(527, Hp21, 900)], [Me(4, 0), Me(527, 100)], 527);
        var second = Fight([B(530, Hp22), B(531, Hp22), B(532, Hp22), B(800, Hp22, 500)], [Me(530, 100), Me(800, 200)], 800, Start.AddSeconds(530));

        var joined = FightRegions.Join([first, second]);

        Assert.Equal(new long[] { 0, 0, 0, 900, 900, 900, 900, 1400 }, joined.Boss.Select(b => b.Taken));
        Assert.Equal(new long?[] { 900, 500 }, FightRegions.Of(joined, Gates).Select(x => x.BossTaken));
    }

    [Fact]
    public void Boss_damage_that_goes_on_in_the_next_file_is_left_as_it_is()
    {
        var first = Fight([B(5, Hp21), B(527, Hp21, 900)], [Me(4, 0), Me(527, 100)], 527);
        var second = Fight([B(530, Hp22, 900), B(800, Hp22, 1400)], [Me(530, 100), Me(800, 200)], 800, Start.AddSeconds(530));

        var joined = FightRegions.Join([first, second]);

        Assert.Equal(new long[] { 0, 900, 900, 1400 }, joined.Boss.Select(b => b.Taken));
    }

    [Fact]
    public void A_small_dip_at_the_file_boundary_is_no_restart()
    {
        var first = Fight([B(5, Hp21), B(527, Hp21, 900)], [Me(4, 0), Me(527, 100)], 527);
        var second = Fight([B(530, Hp22, 880), B(800, Hp22, 1400)], [Me(530, 100), Me(800, 200)], 800, Start.AddSeconds(530));

        var joined = FightRegions.Join([first, second]);

        Assert.Equal(new long[] { 0, 900, 880, 1400 }, joined.Boss.Select(b => b.Taken));
    }

    [Fact]
    public void Each_restart_adds_on_and_a_file_without_boss_reads_changes_nothing()
    {
        var first = Fight([B(5, Hp21), B(100, Hp21, 900)], [Me(4, 0), Me(100, 100)], 100);
        var empty = Fight([], [Me(105, 100), Me(110, 100)], 110, Start.AddSeconds(105));
        var second = Fight([B(115, Hp22), B(200, Hp22, 500)], [Me(115, 100), Me(200, 200)], 200, Start.AddSeconds(115));
        var third = Fight([B(205, Hp22), B(300, Hp22, 300)], [Me(205, 200), Me(300, 300)], 300, Start.AddSeconds(205));

        var joined = FightRegions.Join([first, empty, second, third]);

        Assert.Equal(new long[] { 0, 900, 900, 1400, 1400, 1700 }, joined.Boss.Select(b => b.Taken));
    }

    [Fact]
    public void One_fight_alone_joins_to_itself()
    {
        Assert.Same(Part1, FightRegions.Join([Part1]));
    }

    [Fact]
    public void The_hidden_bar_never_names_a_fight()
    {
        Assert.Equal(Hp22, BossBook.FightHp([.. Reads(4, 10, Hp22), .. Reads(11, 90, 7)]));
    }

    // A wipe in the ??? phase: nothing to read, so 클리어 until the same raid starts again (user, 2026-11-04).
    static FightRecord EndedInHidden => Fight([.. Reads(530, 823, Hp22), .. Reads(824, 975, 7)], [Me(530, 500), Me(975, 900)], 975, gate: "종막: 최후의 날 · EX 2-2관문");

    [Fact]
    public void After_a_hidden_ending_the_same_raid_again_is_a_failure()
    {
        Assert.False(FightVerdict.ByNext(EndedInHidden, Hp21, null, Gates));
    }

    [Fact]
    public void After_a_hidden_ending_another_raid_leaves_the_clear()
    {
        long act4 = Gates.First(g => g.Id == "act4-g1").Hp.Values.First();

        Assert.True(FightVerdict.ByNext(EndedInHidden, act4, null, Gates));
    }

    [Fact]
    public void After_a_hidden_ending_a_boss_no_gate_knows_says_nothing()
    {
        Assert.Null(FightVerdict.ByNext(EndedInHidden, 123_456_789_000, null, Gates));
    }

    [Fact]
    public void A_retry_is_read_by_the_panels_name_too()
    {
        Assert.False(FightVerdict.ByNext(EndedInHidden with { Gate = "최후의 날" }, Hp21, null, Gates));
    }

    [Fact]
    public void Hard_clears_only_after_the_hidden_phase_on_either_name()
    {
        var raids = HomeworkData.Load(Profiles());
        FightRecord Hard(string gate, bool hidden) => new(Start, gate, 900, [], "나", 3, null, [], [],
            hidden ? [.. Reads(4, 400, 900_000_000_000), .. Reads(401, 900, 7)] : [.. Reads(4, 900, 900_000_000_000)], [], Cleared: true, Difficulty: "하드", Character: "살살");

        Assert.NotNull(Homework.ClearOf(Hard("최후의 날", true), Gates, raids));
        Assert.NotNull(Homework.ClearOf(Hard("종막: 최후의 날 · 2-2관문 (하드)", true), Gates, raids));
        Assert.Null(Homework.ClearOf(Hard("종막: 최후의 날 · 2-2관문 (하드)", false), Gates, raids));
        Assert.Null(Homework.ClearOf(Hard("종막: 최후의 날 · 2-1관문", true), Gates, raids));
        Assert.Null(Homework.ClearOf(Hard("4막: 파멸의 성채", true), Gates, raids));
    }

    [Fact]
    public void Recorder_counts_a_fight_ended_on_the_hidden_bar_as_cleared()
    {
        var recorder = new FightRecorder();
        var clock = FightClockState.None;
        var now = TimeSpan.Zero;
        for (int t = 0; t < 12; t++)
        {
            clock = clock with { Timer = t, MovedAt = now, Fight = 1 };
            long max = t < 6 ? Hp22 : 7;
            recorder.Step(MeterStatus.None with { Clock = clock, Boss = BossDamageState.None with { Last = max, Max = max }, HpAt = now }, [], [], 0, null, now, Start);
            now += TimeSpan.FromSeconds(1);
        }

        Assert.True(recorder.Finish()!.Cleared);
    }
}
