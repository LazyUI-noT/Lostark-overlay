using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class SkillFacesTests
{
    static readonly RecordSkill Charge = new("차지 스팅거", "gl_skill/gl_skill_01_30.png", SlotGroup.일반스킬);
    static FightRecord R(RecordSkill? q = null) => new(DateTime.Today, null, 60, [], "me", 1, null, [], [], [], [])
    { Skills = q is null ? null : new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = q } };

    [Fact]
    public void A_fight_from_before_skills_were_saved_draws_the_saved_icon_or_the_key()
    {
        Assert.Equal(SkillFaceKind.Saved, SkillFaces.Of(R(), SlotId.일반1, hasSaved: true, savedName: null, downloaded: false));
        Assert.Equal(SkillFaceKind.Key, SkillFaces.Of(R(), SlotId.일반1, hasSaved: false, savedName: null, downloaded: true));
    }

    [Fact]
    public void The_saved_icon_is_drawn_only_when_it_shows_the_fights_skill()
    {
        Assert.Equal(SkillFaceKind.Saved, SkillFaces.Of(R(Charge), SlotId.일반1, true, "차지 스팅거", false));
        Assert.Equal(SkillFaceKind.Library, SkillFaces.Of(R(Charge), SlotId.일반1, true, "버스트 캐넌", true));   // swapped since
        Assert.Equal(SkillFaceKind.Chip, SkillFaces.Of(R(Charge), SlotId.일반1, true, null, false));
    }

    [Fact]
    public void An_unknown_slot_in_a_fight_with_skills_falls_back_to_the_saved_icon_or_the_key()
    {
        Assert.Equal(SkillFaceKind.Saved, SkillFaces.Of(R(Charge), SlotId.일반2, true, null, true));
        Assert.Equal(SkillFaceKind.Key, SkillFaces.Of(R(Charge), SlotId.일반2, false, null, true));
    }
}
