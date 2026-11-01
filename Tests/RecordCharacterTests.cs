using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>전투 기록 per character (records-timeline.design.md): a record's character and the chips the page shows.</summary>
public class RecordCharacterTests
{
    static FightRecord Rec(string? cls, string? sub, int day = 1, bool kept = false) =>
        new(new DateTime(2026, 11, day, 20, 0, 0), "act4-g1", 300, [], "나", 3, sub, [], [], [], [], kept, cls);

    [Fact]
    public void A_record_is_its_class_and_subclass_or_unknown()
    {
        Assert.Equal("워로드 · 고독한기사", RecordCharacter.Of(Rec("워로드", "고독한기사")));
        Assert.Equal("워로드", RecordCharacter.Of(Rec("워로드", null)));
        Assert.Equal(RecordCharacter.Unknown, RecordCharacter.Of(Rec(null, "고독한기사")));
        Assert.Equal("알 수 없음", RecordCharacter.Unknown);
    }

    [Fact]
    public void Chips_put_the_current_character_first_then_latest_and_unknown_last()
    {
        var list = new[] { Rec("바드", "진실된용맹", day: 3), Rec("워로드", "고독한기사", day: 1), Rec(null, null, day: 4), Rec("워로드", "고독한기사", day: 2, kept: true) };

        var chips = RecordCharacter.Chips(list, current: "워로드 · 고독한기사");

        Assert.Equal(["워로드 · 고독한기사", "바드 · 진실된용맹", "알 수 없음"], chips.Select(c => c.Character));
        Assert.Equal((1, 1), (chips[0].Auto, chips[0].Kept));
        Assert.Equal(new DateTime(2026, 11, 2, 20, 0, 0), chips[0].Latest);
    }

    [Fact]
    public void Label_names_the_character_the_way_its_records_group_under_it()
    {
        Assert.Equal("워로드 · 고독한기사", RecordCharacter.Label("워로드", "고독한기사"));
        Assert.Equal("워로드", RecordCharacter.Label("워로드", ""));
        Assert.Equal(RecordCharacter.Unknown, RecordCharacter.Label("", "고독한기사"));
    }

    [Fact]
    public void Without_a_current_character_chips_go_by_latest_fight()
    {
        var chips = RecordCharacter.Chips([Rec("워로드", "고독한기사", day: 1), Rec("바드", "진실된용맹", day: 3)], current: null);
        Assert.Equal(["바드 · 진실된용맹", "워로드 · 고독한기사"], chips.Select(c => c.Character));
    }
}
