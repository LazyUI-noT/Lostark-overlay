using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class HpChangeTests
{
    const long Normal = 459_549_396_109, Hard = 905_014_093_444;
    static readonly RaidGate Echidna = new("act4-g1", "4막: 파멸의 성채", "1관문", [], [], Raid.없음,
        new Dictionary<string, long> { ["노말"] = Normal, ["하드"] = Hard });

    static long Nerf(long hp, double by) => (long)(hp * (1 - by));

    [Fact]
    public void A_value_near_one_difficulty_is_confirmed_when_the_panel_agrees()
    {
        Assert.Equal(("하드", HpTrust.Confirmed), HpChange.Judge(Echidna, Nerf(Hard, 0.15), "하드"));
        Assert.Equal(("하드", HpTrust.Confirmed), HpChange.Judge(Echidna, Nerf(Hard, 0.25), "하드"));   // the rare 25% nerf
        Assert.Equal(("노말", HpTrust.Confirmed), HpChange.Judge(Echidna, (long)(Normal * 1.2), "노말"));   // a buff the same way
    }

    [Fact]
    public void Without_the_panel_only_a_value_within_15_percent_is_used_unconfirmed()
    {
        Assert.Equal(("하드", HpTrust.Unconfirmed), HpChange.Judge(Echidna, Nerf(Hard, 0.10), null));
        Assert.Null(HpChange.Judge(Echidna, Nerf(Hard, 0.25), null));
    }

    [Fact]
    public void Nothing_is_learned_when_the_panel_and_the_hp_disagree_or_two_values_are_near()
    {
        Assert.Null(HpChange.Judge(Echidna, Nerf(Normal, 0.15), "하드"));
        var close = Echidna with { HpByDifficulty = new Dictionary<string, long> { ["노말"] = 100_000, ["하드"] = 120_000 } };
        Assert.Null(HpChange.Judge(close, 110_000, "하드"));
    }

    [Fact]
    public void A_value_far_from_every_stored_one_is_learned_only_as_a_difficulty_the_gate_has_no_value_for()
    {
        Assert.Equal(("나이트메어", HpTrust.New), HpChange.Judge(Echidna, Hard * 2, "나이트메어"));
        Assert.Null(HpChange.Judge(Echidna, Hard * 2, "하드"));   // 하드 has a value, far away: a misread, not learned
        Assert.Null(HpChange.Judge(Echidna, Hard * 2, null));
        var mordum = Echidna with { HpByDifficulty = null };
        Assert.Equal(("노말", HpTrust.New), HpChange.Judge(mordum, 123_456_789, "노말"));   // a gate with no HP yet gets its first
    }

    [Fact]
    public void The_stored_value_itself_is_nothing_to_learn()
    {
        Assert.Null(HpChange.Judge(Echidna, Hard, "하드"));
    }

    [Fact]
    public void A_value_within_one_percent_of_a_stored_one_is_a_misread_not_a_patch()
    {
        Assert.Null(HpChange.Judge(Echidna, Hard + 4, "하드"));   // the "4 off" row
        Assert.Null(HpChange.Judge(Echidna, Nerf(Hard, 0.009), "하드"));
        Assert.Null(HpChange.Judge(Echidna, Hard - 4, null));
        Assert.Equal(("하드", HpTrust.Confirmed), HpChange.Judge(Echidna, Nerf(Hard, 0.02), "하드"));
    }

    [Fact]
    public void A_max_hp_counts_only_after_three_equal_reads_in_a_row()
    {
        var s = HpStreak.None;
        s = s.Next(Hard); s = s.Next(Hard);
        Assert.False(s.Steady);
        Assert.True(s.Next(Hard).Steady);
        Assert.False(s.Next(Hard + 1).Next(Hard).Steady);   // another value restarts the count
        Assert.False(s.Next(null).Steady);
    }

    [Fact]
    public void Learned_and_this_fights_values_are_merged_into_the_gates()
    {
        var raids = new RaidSettings();
        raids.Gate("act4-g1").LearnedHp = new() { ["하드"] = new(Nerf(Hard, 0.15), Hard) };
        var fight = new List<FightHp> { new("act4-g1", "노말", Nerf(Normal, 0.1), HpTrust.Unconfirmed) };

        var gates = HpChange.Effective([Echidna], raids, fight);

        Assert.Equal(("act4-g1", "하드"), (RaidData.ByHp(gates, Nerf(Hard, 0.15))!.Value.Gate.Id, RaidData.ByHp(gates, Nerf(Hard, 0.15))!.Value.Difficulty));
        Assert.Equal("노말", RaidData.ByHp(gates, Nerf(Normal, 0.1))!.Value.Difficulty);
        Assert.Null(RaidData.ByHp(gates, Hard));   // the old value no longer names it
        Assert.Same(Echidna, HpChange.Effective([Echidna], new RaidSettings(), [])[0]);
    }

    [Fact]
    public void An_update_that_ships_the_learned_value_or_a_newer_one_drops_the_local_value()
    {
        long learned = Nerf(Hard, 0.15);
        RaidSettings Learned(long? replaced)
        {
            var r = new RaidSettings();
            r.Gate("act4-g1").LearnedHp = new() { ["하드"] = new(learned, replaced) };
            return r;
        }
        RaidGate Shipping(long hard) => Echidna with { HpByDifficulty = new Dictionary<string, long> { ["노말"] = Normal, ["하드"] = hard } };

        var same = Learned(Hard);
        Assert.False(HpChange.Prune([Echidna], same));   // raids.json not updated yet: kept
        Assert.Single(same.Gates["act4-g1"].LearnedHp!);

        var shipped = Learned(Hard);
        Assert.True(HpChange.Prune([Shipping(learned)], shipped));
        Assert.Empty(shipped.Gates["act4-g1"].LearnedHp!);

        var newer = Learned(Hard);
        Assert.True(HpChange.Prune([Shipping(learned - 5)], newer));   // the update knows better
        Assert.Empty(newer.Gates["act4-g1"].LearnedHp!);

        var first = Learned(null);   // learned as a difficulty raids.json had no value for
        Assert.True(HpChange.Prune([Shipping(Hard)], first));
    }

    [Fact]
    public void Only_confirmed_values_are_kept_with_the_raids_json_value_they_replace()
    {
        var raids = new RaidSettings();
        List<FightHp> fight =
        [
            new("act4-g1", "하드", Nerf(Hard, 0.15), HpTrust.Confirmed),
            new("act4-g1", "노말", Nerf(Normal, 0.1), HpTrust.Unconfirmed),
            new("act4-g1", "나이트메어", Hard * 2, HpTrust.New),
        ];

        Assert.True(HpChange.Keep([Echidna], raids, fight, Day1));
        var kept = raids.Gates["act4-g1"].LearnedHp!;
        Assert.Equal(new LearnedHp(Nerf(Hard, 0.15), Hard), kept["하드"]);
        Assert.False(kept.ContainsKey("나이트메어"));   // a first value waits for a later raid
        Assert.False(kept.ContainsKey("노말"));
        Assert.False(HpChange.Keep([Echidna], raids, fight, Day1));   // nothing new: no save
    }

    [Fact]
    public void Reading_raids_json_value_again_drops_the_learned_one_instead_of_listing_it_as_a_change()
    {
        var raids = new RaidSettings();
        raids.Gate("act4-g1").LearnedHp = new() { ["하드"] = new(Nerf(Hard, 0.15), Hard) };
        // Judge runs on the effective HP (learned merged in), so raids.json's own value reads as near the learned one.
        List<FightHp> fight = [new("act4-g1", "하드", Hard, HpTrust.Confirmed)];

        Assert.True(HpChange.Keep([Echidna], raids, fight, Day1));

        Assert.Empty(raids.Gates["act4-g1"].LearnedHp!);
        Assert.Empty(HpChange.Found([Echidna], raids));   // no "905,014,093,444 → 905,014,093,444" row
    }

    static readonly DungeonInfo HardPanel = new(DungeonKind.Raid, "파멸의 성채", "하드");

    static HpLearner Steps(HpLearner l, int n, long hp, string? byName, DungeonInfo? panel)
    {
        for (int i = 0; i < n; i++) l = l.Step([Echidna], new RaidSettings(), "act4-g1", hp, byName, panel);
        return l;
    }

    [Fact]
    public void Three_steady_reads_with_the_gates_name_read_learn_a_value()
    {
        long hp = Nerf(Hard, 0.15);
        Assert.Empty(Steps(HpLearner.None, 2, hp, "act4-g1", HardPanel).Fight);
        Assert.Equal([new FightHp("act4-g1", "하드", hp, HpTrust.Confirmed)], Steps(HpLearner.None, 3, hp, "act4-g1", HardPanel).Fight);
        Assert.Empty(Steps(HpLearner.None, 3, hp, byName: null, HardPanel).Fight);        // no name read while it showed
        Assert.Empty(Steps(HpLearner.None, 3, hp, byName: "act4-g2", HardPanel).Fight);   // the name read another gate
    }

    [Fact]
    public void An_unconfirmed_value_without_a_panel_stays_without_new_events()
    {
        long hp = Nerf(Hard, 0.1);
        var learned = Steps(HpLearner.None, 3, hp, "act4-g1", panel: null);
        Assert.Equal([new FightHp("act4-g1", "하드", hp, HpTrust.Unconfirmed)], learned.Fight);
        var later = Steps(learned, 5, hp, byName: null, panel: null);
        Assert.Same(learned.Fight, later.Fight);   // same list: no event, no 설정 rebuild
    }

    [Fact]
    public void Another_panel_clears_the_raids_values_and_the_panel_can_then_confirm()
    {
        long hp = Nerf(Hard, 0.1);
        var unconfirmed = Steps(HpLearner.None, 3, hp, "act4-g1", panel: null);
        var shown = unconfirmed.Step([Echidna], new RaidSettings(), "act4-g1", hp, "act4-g1", HardPanel);
        Assert.Empty(shown.Fight);   // the panel appeared: what was learned without it starts over
        Assert.Equal([new FightHp("act4-g1", "하드", hp, HpTrust.Confirmed)], Steps(shown, 3, hp, "act4-g1", HardPanel).Fight);
        Assert.Empty(Steps(HpLearner.None, 3, hp, "act4-g1", HardPanel)
            .Step([Echidna], new RaidSettings(), null, null, null, new DungeonInfo(DungeonKind.Guardian, null, null)).Fight);
    }

    [Fact]
    public void An_undo_keeps_the_hp_values_learned_meanwhile()
    {
        var live = new Settings();
        var before = new Settings();
        live.Raids.Gate("act4-g1").LearnedHp = new() { ["하드"] = new(1, Hard) };
        live.Raids.Gate("act4-g1").PendingHp = new() { ["나이트메어"] = new(2, Day1) };
        live.CopyFrom(before);
        Assert.Equal(new LearnedHp(1, Hard), live.Raids.Gates["act4-g1"].LearnedHp!["하드"]);
        Assert.Equal(new PendingHp(2, Day1), live.Raids.Gates["act4-g1"].PendingHp!["나이트메어"]);
    }

    static readonly DateTime Day1 = new(2026, 11, 4, 20, 0, 0);

    [Fact]
    public void A_first_value_is_saved_only_when_a_later_raid_reads_it_again()
    {
        var raids = new RaidSettings();
        List<FightHp> fight = [new("act4-g1", "나이트메어", Hard * 2, HpTrust.New)];

        Assert.True(HpChange.Keep([Echidna], raids, fight, Day1));
        Assert.Equal(new PendingHp(Hard * 2, Day1), raids.Gates["act4-g1"].PendingHp!["나이트메어"]);
        Assert.Null(raids.Gates["act4-g1"].LearnedHp?.GetValueOrDefault("나이트메어"));
        Assert.Null(RaidData.ByHp(HpChange.Effective([Echidna], raids, []), Hard * 2));   // pending: not used outside a raid that reads it

        Assert.False(HpChange.Keep([Echidna], raids, fight, Day1.AddHours(1)));   // the same raid (a re-read within hours)
        Assert.Null(raids.Gates["act4-g1"].LearnedHp?.GetValueOrDefault("나이트메어"));

        Assert.True(HpChange.Keep([Echidna], raids, fight, Day1.AddDays(7)));   // next week's raid
        Assert.Equal(new LearnedHp(Hard * 2, null), raids.Gates["act4-g1"].LearnedHp!["나이트메어"]);
        Assert.Empty(raids.Gates["act4-g1"].PendingHp!);
    }

    [Fact]
    public void Another_first_value_replaces_the_pending_one_and_starts_over()
    {
        var raids = new RaidSettings();
        HpChange.Keep([Echidna], raids, [new("act4-g1", "나이트메어", Hard * 2, HpTrust.New)], Day1);
        Assert.True(HpChange.Keep([Echidna], raids, [new("act4-g1", "나이트메어", Hard * 3, HpTrust.New)], Day1.AddDays(7)));
        Assert.Equal(new PendingHp(Hard * 3, Day1.AddDays(7)), raids.Gates["act4-g1"].PendingHp!["나이트메어"]);
        Assert.Null(raids.Gates["act4-g1"].LearnedHp);
    }

    [Fact]
    public void A_first_value_needs_two_name_reads_in_the_raid()
    {
        long hp = Hard * 2;
        var nightmare = new DungeonInfo(DungeonKind.Raid, "파멸의 성채", "나이트메어");
        var once = Steps(HpLearner.None, 2, hp, byName: null, nightmare).Step([Echidna], new RaidSettings(), "act4-g1", hp, "act4-g1", nightmare);
        Assert.Empty(once.Fight);   // steady HP, one name read
        var twice = once.Step([Echidna], new RaidSettings(), "act4-g1", hp, "act4-g1", nightmare);
        Assert.Equal([new FightHp("act4-g1", "나이트메어", hp, HpTrust.New)], twice.Fight);
    }

    [Fact]
    public void An_update_with_a_value_for_a_pending_difficulty_drops_the_pending_one()
    {
        var raids = new RaidSettings();
        raids.Gate("act4-g1").PendingHp = new() { ["하드"] = new(Hard * 2, Day1) };
        Assert.True(HpChange.Prune([Echidna], raids));
        Assert.Empty(raids.Gates["act4-g1"].PendingHp!);
    }

    [Fact]
    public void The_copy_line_is_the_gates_hp_as_raids_json_writes_it()
    {
        Assert.Equal("\"hp\": { \"노말\": 459549396109, \"하드\": 905014093444 }", HpChange.HpLine(Echidna));
    }
}
