using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The meter's 비교 line (lostark-api.plan.md phase 6): which saved fight it shows and its my-DPS curve.</summary>
public class MeterCompareTests
{
    const string Me = "너무아파요살살";

    static (string File, FightRecord Record) Saved(string file, int day, string? character = Me, string? gate = "카제로스 3막 3관",
        string? difficulty = "하드", bool? cleared = true) =>
        (file, new FightRecord(new DateTime(2026, 11, day, 20, 0, 0), gate, 300, [], "나", 3, "고독한기사", [], [], [], [],
            Difficulty: difficulty, Cleared: cleared, Character: character));

    static readonly (string, FightRecord)[] Records =
    [
        Saved("a", 1),
        Saved("b", 3, cleared: false),   // the newest: failed fights count too
        Saved("c", 2),
        Saved("d", 5, character: "다른캐릭"),
        Saved("e", 6, difficulty: "노말"),
        Saved("f", 7, gate: "카제로스 3막 2관"),
    ];

    [Fact]
    public void Auto_picks_the_most_recent_fight_of_the_same_raid_and_character_failed_ones_included()
    {
        var pick = MeterCompare.Pick(Records, Me, "카제로스 3막 3관", "하드", chosen: null);

        Assert.Equal(("b", MeterCompare.Kind.자동), (pick?.File, pick?.How));
    }

    [Fact]
    public void A_chosen_fight_is_kept_while_it_is_still_a_candidate()
    {
        var pick = MeterCompare.Pick(Records, Me, "카제로스 3막 3관", "하드", chosen: "a");

        Assert.Equal(("a", MeterCompare.Kind.선택), (pick?.File, pick?.How));
    }

    [Fact]
    public void A_chosen_fight_gone_or_of_another_raid_falls_back_to_auto()
    {
        Assert.Equal("b", MeterCompare.Pick(Records, Me, "카제로스 3막 3관", "하드", chosen: "deleted")?.File);
        Assert.Equal(MeterCompare.Kind.자동, MeterCompare.Pick(Records, Me, "카제로스 3막 3관", "하드", chosen: "f")?.How);
    }

    [Fact]
    public void No_candidate_no_character_or_no_gate_gives_nothing()
    {
        Assert.Null(MeterCompare.Pick(Records, "없는캐릭", "카제로스 3막 3관", "하드", null));
        Assert.Null(MeterCompare.Pick(Records, null, "카제로스 3막 3관", "하드", null));
        Assert.Null(MeterCompare.Pick([Saved("u", 1, gate: null)], Me, null, "하드", null));
    }

    [Fact]
    public void The_ghost_is_my_dps_per_fight_second_in_time_order()
    {
        var r = Saved("a", 1).Record with
        {
            Samples = [new(2, "나", 200, 20), new(1, "나", 100, 10), new(1, "남", 999, 99)],
        };

        Assert.Equal([(TimeSpan.FromSeconds(1), 10L), (TimeSpan.FromSeconds(2), 20L)], MeterCompare.Ghost(r));
    }

    [Fact]
    public void Choosing_and_going_back_to_auto_return_new_maps_per_character_and_raid()
    {
        var empty = new Dictionary<string, string>();

        var chosen = MeterCompare.Choose(empty, Saved("a", 1).Record, "a");
        var key = MeterCompare.Key(Me, "카제로스 3막 3관", "하드");

        Assert.Empty(empty);
        Assert.Equal("a", chosen[key]);
        Assert.True(MeterCompare.IsChosen(chosen, Saved("a", 1).Record, "a"));
        Assert.Empty(MeterCompare.Auto(chosen, Saved("a", 1).Record));
        Assert.Single(chosen);
    }

    [Fact]
    public void In_use_is_the_pick_else_the_newest_of_that_gate_and_difficulty()
    {
        (string, FightRecord)[] all = [Saved("b", 2), Saved("a", 1), Saved("n", 3, difficulty: "노말")];
        var none = new Dictionary<string, string>();
        Assert.Equal("b", MeterCompare.InUse(all, none, all[1].Item2));
        Assert.Equal("a", MeterCompare.InUse(all, MeterCompare.Choose(none, all[1].Item2, "a"), all[0].Item2));
        Assert.Equal("n", MeterCompare.InUse(all, none, all[2].Item2));   // another difficulty: its own
        Assert.Null(MeterCompare.InUse(all, none, Saved("x", 1, gate: null).Record));
    }

    [Fact]
    public void A_fight_without_character_or_gate_cannot_be_chosen()
    {
        Assert.False(MeterCompare.CanChoose(Saved("a", 1, character: null).Record));
        Assert.False(MeterCompare.CanChoose(Saved("a", 1, gate: null).Record));
        Assert.True(MeterCompare.CanChoose(Saved("a", 1).Record));
    }

    [Fact]
    public void The_graph_label_names_how_the_fight_was_picked()
    {
        Assert.Equal("비교 · 자동", MeterCompare.Label(MeterCompare.Kind.자동));
        Assert.Equal("비교 · 선택", MeterCompare.Label(MeterCompare.Kind.선택));
    }
}
