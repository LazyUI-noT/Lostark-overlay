using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>아이콘으로 표시: when a slot's icon is copied for the overlay, and which slots a 직업각인's saved icons must cover.</summary>
public class SkillIconTests
{
    private const SlotId Q = SlotId.일반1;
    private static readonly SlotId[] NoAden = [];
    private static readonly Dictionary<SlotGroup, bool> AllShown = [];

    private static Reading R(ReadKind kind, double t, SlotId slot = Q) => new(slot, kind, 0, S(t));

    [Fact]
    public void A_ready_slot_not_copied_yet_is_copied() =>
        Assert.True(SkillIcon.Due(Q, R(ReadKind.Ready, 1), sent: false, NoAden));

    [Fact]
    public void A_slot_already_copied_is_not_copied_again_even_on_a_cooldown_to_ready_edge()
    {
        var s = CooldownTracker.Update(new SlotState(Q, SlotPhase.Cooldown, S(1), null, false, S(0)), R(ReadKind.Ready, 1));
        Assert.Equal(SlotPhase.Ready, s.Phase);
        Assert.False(SkillIcon.Due(Q, R(ReadKind.Ready, 1), sent: true, NoAden));
    }

    [Theory]
    [InlineData(ReadKind.Seconds)]
    [InlineData(ReadKind.Unreadable)]
    [InlineData(ReadKind.Unavailable)]
    public void Only_a_ready_read_is_copied(ReadKind kind) =>
        Assert.False(SkillIcon.Due(Q, R(kind, 1), sent: false, NoAden));

    [Fact]
    public void Aden_skill_slot_is_never_copied() =>
        Assert.False(SkillIcon.Due(SlotId.아덴1, R(ReadKind.Ready, 1, SlotId.아덴1), sent: false, [SlotId.아덴1]));

    [Fact]
    public void Aden_key_without_an_aden_skill_is_copied_like_a_normal_skill() =>
        Assert.True(SkillIcon.Due(SlotId.아덴1, R(ReadKind.Ready, 1, SlotId.아덴1), sent: false, NoAden));

    [Fact]
    public void Needed_slots_are_those_of_the_groups_shown_as_icons()
    {
        var needed = SkillIcon.Needed([SlotGroup.일반스킬], AllShown, new HashSet<SlotId>(), [SlotId.아덴1, SlotId.아덴2]);
        Assert.Equal([SlotId.일반1, SlotId.일반2, SlotId.일반3, SlotId.일반4, SlotId.일반5, SlotId.일반6, SlotId.일반7, SlotId.일반8],
            needed.Order());
    }

    [Fact]
    public void Hidden_slots_hidden_groups_and_aden_skills_are_not_needed()
    {
        var groups = new Dictionary<SlotGroup, bool> { [SlotGroup.초각스] = false };
        var needed = SkillIcon.Needed([SlotGroup.각성기, SlotGroup.초각스, SlotGroup.일반스킬, SlotGroup.아덴스킬], groups,
            new HashSet<SlotId> { SlotId.일반8 }, [SlotId.아덴1, SlotId.아덴2]);
        Assert.Contains(SlotId.각성기, needed);
        Assert.DoesNotContain(SlotId.초각스, needed);
        Assert.DoesNotContain(SlotId.일반8, needed);
        Assert.DoesNotContain(SlotId.아덴1, needed);
    }

    [Fact]
    public void No_group_shown_as_icons_needs_nothing() =>
        Assert.Empty(SkillIcon.Needed([], AllShown, new HashSet<SlotId>(), NoAden));

    [Fact]
    public void Saved_icons_are_complete_only_when_every_needed_slot_has_one()
    {
        SlotId[] needed = [SlotId.일반1, SlotId.일반2];
        Assert.True(SkillIcon.Complete(new HashSet<SlotId> { SlotId.일반1, SlotId.일반2, SlotId.각성기 }, needed));
        Assert.False(SkillIcon.Complete(new HashSet<SlotId> { SlotId.일반1 }, needed));
        Assert.False(SkillIcon.Complete(new HashSet<SlotId>(), needed));
        Assert.True(SkillIcon.Complete(new HashSet<SlotId>(), []));
    }
}
