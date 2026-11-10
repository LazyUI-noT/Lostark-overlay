using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class RecordEditsTests
{
    /// <summary>A fight as stored: each call makes new lists, so equal fights are never the same objects.</summary>
    static FightRecord Saved() => new(DateTime.Now, "베히모스", 600, [], "나", 3, "광전사", [new RecordMember("나", 3, null, 0)], [], [], [],
        MyClassName: "버서커", Difficulty: "노말", Cleared: true, Character: "나");

    static readonly RecordSkill Pick = new("질풍참", "icon", SlotGroup.일반스킬);

    [Fact]
    public void A_fight_as_stored_has_nothing_to_apply()
    {
        Assert.False(RecordEdits.Unapplied(Saved(), Saved()));
    }

    [Fact]
    public void A_changed_dungeon_or_difficulty_is_unapplied()
    {
        Assert.True(RecordEdits.Unapplied(Saved(), Saved() with { Gate = "카제로스" }));
        Assert.True(RecordEdits.Unapplied(Saved(), Saved() with { Difficulty = "하드" }));
    }

    [Fact]
    public void A_blank_name_counts_as_no_name()
    {
        Assert.False(RecordEdits.Unapplied(Saved() with { Gate = null }, Saved() with { Gate = "  " }));
        Assert.False(RecordEdits.Unapplied(Saved() with { Character = "나" }, Saved() with { Character = " 나 " }));
    }

    [Fact]
    public void Setting_or_clearing_the_result_is_unapplied()
    {
        Assert.True(RecordEdits.Unapplied(Saved(), Saved() with { Cleared = false }));
        Assert.True(RecordEdits.Unapplied(Saved(), Saved() with { Cleared = null }));
    }

    [Fact]
    public void Another_character_or_class_or_subclass_is_unapplied()
    {
        Assert.True(RecordEdits.Unapplied(Saved(), Saved() with { Character = "다른캐" }));
        Assert.True(RecordEdits.Unapplied(Saved(), Saved() with { MyClass = 7, MyClassName = "소서리스" }));
        Assert.True(RecordEdits.Unapplied(Saved(), Saved() with { MySubclass = "다른 각인" }));
    }

    [Fact]
    public void A_class_moved_into_the_member_list_is_unapplied()
    {
        Assert.True(RecordEdits.Unapplied(Saved(), Saved() with { Members = [new RecordMember("나", 7, null, 0)] }));
    }

    [Fact]
    public void A_skill_picked_from_the_unsure_candidates_is_unapplied()
    {
        var unsure = Saved() with { Unsure = new Dictionary<SlotId, IReadOnlyList<RecordSkill>> { [SlotId.일반1] = [Pick] } };
        var picked = Saved() with { Skills = new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = Pick } };

        Assert.True(RecordEdits.Unapplied(unsure, picked));
        Assert.False(RecordEdits.Unapplied(picked, Saved() with { Skills = new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = Pick } }));
    }

    [Fact]
    public void A_dropped_proposal_is_unapplied()
    {
        Assert.True(RecordEdits.Unapplied(Saved() with { ProposedClass = "버서커 · 광전사" }, Saved() with { ProposedClass = null }));
    }

    [Fact]
    public void Fields_the_editor_does_not_change_are_not_unapplied()
    {
        Assert.False(RecordEdits.Unapplied(Saved(), Saved() with { Duration = 999, Kept = true, AmsuOn = true }));
    }
}
