using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>HP 변경 감지's list (settings-reorg.plan.md 10): the values found, how many raids saw each, 확정 and 되돌리기, and the
/// boss HP table.</summary>
public class HpChangeConfirmTests
{
    const long Normal = 459_549_396_109, Hard = 905_014_093_444;
    static readonly RaidGate Echidna = new("act4-g1", "4막: 파멸의 성채", "1관문", [], [], Raid.없음,
        new Dictionary<string, long> { ["노말"] = Normal, ["하드"] = Hard });
    static readonly DateTime Day1 = new(2026, 11, 4, 20, 0, 0);
    const long Nerfed = 800_000_000_000;

    static RaidSettings WithLearned(bool fixedValue = false)
    {
        var r = new RaidSettings();
        r.Gate("act4-g1").LearnedHp = new() { ["하드"] = new(Nerfed, Hard, 1, fixedValue) };
        return r;
    }

    [Fact]
    public void Each_raid_that_reads_a_learned_value_again_counts_once()
    {
        var raids = new RaidSettings();
        List<FightHp> fight = [new("act4-g1", "하드", Nerfed, HpTrust.Confirmed)];

        HpChange.Keep([Echidna], raids, fight, Day1, before: []);
        Assert.Equal(1, raids.Gates["act4-g1"].LearnedHp!["하드"].Raids);
        Assert.False(HpChange.Keep([Echidna], raids, fight, Day1, before: fight));   // the same raid again: not counted
        Assert.True(HpChange.Keep([Echidna], raids, fight, Day1.AddMinutes(30), before: []));   // another character's raid
        Assert.Equal(2, raids.Gates["act4-g1"].LearnedHp!["하드"].Raids);
    }

    [Fact]
    public void A_pending_first_value_counts_raids_too_and_carries_them_when_learned()
    {
        var raids = new RaidSettings();
        List<FightHp> fight = [new("act4-g1", "나이트메어", Hard * 2, HpTrust.New)];

        HpChange.Keep([Echidna], raids, fight, Day1, before: []);
        Assert.True(HpChange.Keep([Echidna], raids, fight, Day1.AddHours(1), before: []));   // a second character the same evening
        Assert.Equal(2, raids.Gates["act4-g1"].PendingHp!["나이트메어"].Raids);
        HpChange.Keep([Echidna], raids, fight, Day1.AddDays(7), before: []);
        Assert.Equal(3, raids.Gates["act4-g1"].LearnedHp!["나이트메어"].Raids);
    }

    [Fact]
    public void Found_lists_learned_and_pending_values_but_not_confirmed_ones()
    {
        var raids = WithLearned();
        raids.Gate("act4-g1").PendingHp = new() { ["나이트메어"] = new(Hard * 2, Day1, 2) };

        var found = HpChange.Found([Echidna], raids);
        Assert.Equal(2, found.Count);
        Assert.Contains(new HpFound(Echidna, "하드", Hard, Nerfed, 1, Pending: false), found);
        Assert.Contains(new HpFound(Echidna, "나이트메어", null, Hard * 2, 2, Pending: true), found);

        Assert.True(HpChange.Confirm([Echidna], raids, "act4-g1", "하드"));
        Assert.DoesNotContain(HpChange.Found([Echidna], raids), f => f.Difficulty == "하드");
    }

    [Fact]
    public void Confirming_a_pending_value_makes_it_learned_and_used()
    {
        var raids = new RaidSettings();
        raids.Gate("act4-g1").PendingHp = new() { ["나이트메어"] = new(Hard * 2, Day1, 2) };

        Assert.True(HpChange.Confirm([Echidna], raids, "act4-g1", "나이트메어"));
        Assert.Equal(new LearnedHp(Hard * 2, null, 2, Fixed: true), raids.Gates["act4-g1"].LearnedHp!["나이트메어"]);
        Assert.Empty(raids.Gates["act4-g1"].PendingHp!);
        Assert.NotNull(RaidData.ByHp(HpChange.Effective([Echidna], raids, []), Hard * 2));
        Assert.False(HpChange.Confirm([Echidna], raids, "act4-g1", "노말"));   // nothing there
    }

    [Fact]
    public void A_confirmed_value_outlives_an_update_with_another_value()
    {
        var raids = WithLearned(fixedValue: true);
        var update = Echidna with { HpByDifficulty = new Dictionary<string, long> { ["노말"] = Normal, ["하드"] = Hard - 5 } };
        Assert.False(HpChange.Prune([update], raids));
        Assert.Equal(Nerfed, HpChange.Effective([update], raids, [])[0].Hp["하드"]);

        var same = Echidna with { HpByDifficulty = new Dictionary<string, long> { ["노말"] = Normal, ["하드"] = Nerfed } };
        Assert.True(HpChange.Prune([same], raids));   // shipped now: nothing left to keep
    }

    [Fact]
    public void Undo_goes_back_to_the_shipped_value()
    {
        var raids = WithLearned(fixedValue: true);
        Assert.True(HpChange.Undo(raids, "act4-g1", "하드"));
        Assert.Equal(Hard, HpChange.Effective([Echidna], raids, [])[0].Hp["하드"]);
        Assert.False(HpChange.Undo(raids, "act4-g1", "하드"));
    }

    [Fact]
    public void The_table_has_every_difficulty_with_its_shipped_and_confirmed_value()
    {
        var raids = WithLearned(fixedValue: true);
        raids.Gate("act4-g1").LearnedHp!["노말"] = new(Normal - 1, Normal);   // learned, not 확정: shipped only in the table

        var table = HpChange.Table([Echidna], raids);
        Assert.Equal([new HpTableRow(Echidna, "노말", Normal, null), new HpTableRow(Echidna, "하드", Hard, Nerfed)], table);
    }

    [Fact]
    public void A_learned_value_saved_by_an_older_version_reads_as_one_raid_and_not_confirmed()
    {
        var old = JsonSerializer.Deserialize<LearnedHp>("""{ "Hp": 5, "Replaced": 7 }""", SettingsStore.Json)!;
        Assert.Equal(new LearnedHp(5, 7, 1, false), old);
        var pending = JsonSerializer.Deserialize<PendingHp>("""{ "Hp": 5, "Seen": "2026-11-04T20:00:00Z" }""", SettingsStore.Json)!;
        Assert.Equal(1, pending.Raids);
    }

    [Fact]
    public void A_value_typed_in_the_table_is_kept_as_confirmed()
    {
        var raids = new RaidSettings();

        Assert.True(HpChange.SetFixed([Echidna], raids, "act4-g1", "하드", Nerfed));

        var row = Assert.Single(HpChange.Table([Echidna], raids), x => x.Difficulty == "하드");
        Assert.Equal(Nerfed, row.Fixed);
        Assert.Equal(Hard, raids.Gates["act4-g1"].LearnedHp!["하드"].Replaced);
    }

    [Fact]
    public void Typing_the_shipped_value_back_goes_back_to_it()
    {
        var raids = WithLearned(fixedValue: true);

        Assert.True(HpChange.SetFixed([Echidna], raids, "act4-g1", "하드", Hard));

        Assert.Null(Assert.Single(HpChange.Table([Echidna], raids), x => x.Difficulty == "하드").Fixed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_value_that_is_no_HP_is_refused(long hp)
    {
        var raids = new RaidSettings();

        Assert.False(HpChange.SetFixed([Echidna], raids, "act4-g1", "하드", hp));
        Assert.False(raids.Gates.ContainsKey("act4-g1"));
    }

    [Theory]
    [InlineData("762920268298", 762_920_268_298)]
    [InlineData(" 762,920,268,298 ", 762_920_268_298)]
    [InlineData("1조 2985억", 1_298_500_000_000)]
    [InlineData("7629억", 762_900_000_000)]
    [InlineData("1조2985억 5000만", 1_298_550_000_000)]
    public void A_typed_HP_reads_digits_or_the_game_units(string text, long hp) => Assert.Equal(hp, HpChange.ParseHp(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("억")]
    [InlineData("1조 abc")]
    [InlineData("99999999999999999999")]
    [InlineData("１조")]   // full-width digits: not read, never a crash
    public void Text_that_is_no_number_reads_as_nothing(string text) => Assert.Null(HpChange.ParseHp(text));

    [Fact]
    public void Setting_the_value_already_kept_changes_nothing()
    {
        var raids = WithLearned(fixedValue: true);

        Assert.False(HpChange.SetFixed([Echidna], raids, "act4-g1", "하드", Nerfed));
    }
}
