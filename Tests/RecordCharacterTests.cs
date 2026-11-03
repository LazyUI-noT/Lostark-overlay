using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>전투 기록 per character (homework.plan.md): a record's in-game character, its class label, and the chips the page shows.</summary>
public class RecordCharacterTests
{
    static FightRecord Rec(string? name, int day = 1, bool kept = false, string? cls = "워로드", string? sub = "고독한기사") =>
        new(new DateTime(2026, 11, day, 20, 0, 0), "act4-g1", 300, [], "나", 3, sub, [], [], [], [], kept, cls, Character: name);

    [Fact]
    public void A_record_is_its_character_name_or_unknown()
    {
        Assert.Equal("너무아파요살살", RecordCharacter.Of(Rec("너무아파요살살")));
        Assert.Equal(RecordCharacter.Unknown, RecordCharacter.Of(Rec(null)));
        Assert.Equal(RecordCharacter.Unknown, RecordCharacter.Of(Rec("  ")));
        Assert.Equal("캐릭터 불명", RecordCharacter.Unknown);
    }

    [Fact]
    public void ClassOf_is_the_class_and_subclass_the_fight_was_played_on()
    {
        Assert.Equal("워로드 · 고독한기사", RecordCharacter.ClassOf(Rec("a")));
        Assert.Equal("워로드", RecordCharacter.ClassOf(Rec("a", sub: null)));
        Assert.Equal(RecordCharacter.NoClass, RecordCharacter.ClassOf(Rec("a", cls: null)));
    }

    [Fact]
    public void Chips_put_the_current_character_first_then_latest_and_unknown_last()
    {
        var list = new[] { Rec("바드짱", day: 3), Rec("살살", day: 1), Rec(null, day: 4), Rec("살살", day: 2, kept: true) };

        var chips = RecordCharacter.Chips(list, current: "살살");

        Assert.Equal(["살살", "바드짱", RecordCharacter.Unknown], chips.Select(c => c.Character));
        Assert.Equal((1, 1), (chips[0].Auto, chips[0].Kept));
        Assert.Equal(new DateTime(2026, 11, 2, 20, 0, 0), chips[0].Latest);
    }

    [Fact]
    public void Label_names_a_class_and_subclass()
    {
        Assert.Equal("워로드 · 고독한기사", RecordCharacter.Label("워로드", "고독한기사"));
        Assert.Equal("워로드", RecordCharacter.Label("워로드", ""));
        Assert.Equal(RecordCharacter.NoClass, RecordCharacter.Label("", "고독한기사"));
    }

    [Fact]
    public void Without_a_current_character_chips_go_by_latest_fight()
    {
        var chips = RecordCharacter.Chips([Rec("살살", day: 1), Rec("바드짱", day: 3)], current: null);
        Assert.Equal(["바드짱", "살살"], chips.Select(c => c.Character));
    }
}
