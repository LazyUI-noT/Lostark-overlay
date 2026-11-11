using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class RaidOrderTests
{
    static readonly RaidGate[] Gates =
    [
        new("b1", "벨가르딘", "1관문", [], [], Raid.없음), new("b2", "벨가르딘", "2관문", [], [], Raid.없음),
        new("s1", "고통의 마녀", "1관문", [], [], Raid.없음),
    ];

    static HomeworkLevel L(string d, int entry) => new(d, d, HomeworkColor.White, [], entry);

    static readonly HomeworkRaid[] Raids =
    [
        new("serka", "세르카", ["s1"], [L("노말", 1710), L("하드", 1730)]),
        new("belgardin", "벨가르딘", ["b1", "b2"], [L("매칭", 1750), L("노말", 1750), L("하드", 1770), L("나이트메어", 1780)]),
        new("kamen", "카멘", [], [L("노말", 1610)]),   // no gates: never a group
    ];

    const string B1 = "벨가르딘 · 1관문", B2 = "벨가르딘 · 2관문", S1 = "고통의 마녀 · 1관문";

    [Fact]
    public void Groups_by_raid_highest_entry_first_then_gate_then_difficulty_high_to_low_newest_first_inside()
    {
        (string Name, string? Gate, string? Difficulty)[] newestFirst =
        [
            ("g", B2, "하드"), ("f", S1, "노말"), ("e", B1, "나이트메어"), ("d", B1, "하드"), ("c", null, null), ("b", B2, "나이트메어"), ("a", B1, "하드"),
        ];
        var groups = RaidOrder.Group(newestFirst, x => x.Gate, x => x.Difficulty, Gates, Raids, []);
        Assert.Equal(["belgardin", "serka", null], groups.Select(g => g.Raid?.Id));
        Assert.Equal(["e", "d", "a", "b", "g"], groups[0].Fights.Select(x => x.Name));
        Assert.Equal(["c"], groups[2].Fights.Select(x => x.Name));
    }

    [Fact]
    public void A_raid_with_no_levels_sorts_last_without_throwing()
    {
        HomeworkRaid[] raids = [new("bare", "빈 레이드", ["s1"], []), .. Raids];
        Assert.Equal(["belgardin", "serka", "bare"], RaidOrder.Raids(raids, []).Select(r => r.Id));
    }

    [Fact]
    public void A_difficulty_the_raid_doesnt_list_goes_last_in_its_gate()
    {
        (string Name, string? Gate, string? Difficulty)[] newestFirst = [("b", B1, "???"), ("a", B1, "노말")];
        var groups = RaidOrder.Group(newestFirst, x => x.Gate, x => x.Difficulty, Gates, Raids, []);
        Assert.Equal(["a", "b"], groups[0].Fights.Select(x => x.Name));
    }

    [Fact]
    public void The_users_order_comes_first_and_move_swaps_neighbours()
    {
        Assert.Equal(["serka", "belgardin"], RaidOrder.Raids(Raids, ["serka"]).Select(r => r.Id));
        Assert.Equal(["serka", "belgardin"], RaidOrder.Move(Raids, [], "serka", -1));
        Assert.Equal(["belgardin", "serka"], RaidOrder.Move(Raids, [], "belgardin", -1));   // already first: unchanged
    }

    [Fact]
    public void Difficulties_are_the_gates_raids_highest_first_or_null_for_an_unknown_gate()
    {
        Assert.Equal(["나이트메어", "하드", "노말", "매칭"], RaidOrder.Difficulties(B2, Gates, Raids));
        Assert.Null(RaidOrder.Difficulties("알 수 없음", Gates, Raids));
        Assert.Null(RaidOrder.Difficulties(null, Gates, Raids));
    }
}
