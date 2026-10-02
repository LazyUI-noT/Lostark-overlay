using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>레이아웃 편집기의 칸 옵션: a clicked box's colour and effects, for that box alone (the eight 일반스킬) or its
/// whole group.</summary>
public class BoxOptionsTests
{
    static readonly SlotId[] Aden = [SlotId.아덴1, SlotId.아덴2];

    [Fact]
    public void A_box_part_names_its_slot_and_group_and_other_parts_none()
    {
        Assert.Equal((SlotId.일반3, SlotGroup.일반스킬), BoxOptions.Of(Part.일반3, Aden));
        Assert.Equal((SlotId.아덴1, SlotGroup.아덴스킬), BoxOptions.Of(Part.아덴1, Aden));
        Assert.Equal((SlotId.아덴1, SlotGroup.일반스킬), BoxOptions.Of(Part.아덴1, []));
        Assert.Null(BoxOptions.Of(Part.게이지, Aden));
        Assert.Null(BoxOptions.Of(Part.버프공증, Aden));
        Assert.Null(BoxOptions.Of(Part.변화, Aden));
    }

    [Fact]
    public void Only_the_일반스킬_boxes_can_differ_from_their_group()
    {
        Assert.True(BoxOptions.PerBox(SlotGroup.일반스킬));
        Assert.False(BoxOptions.PerBox(SlotGroup.각성기));
    }

    [Fact]
    public void Shine_for_one_box_leaves_its_group_alone()
    {
        var p = new MineProfile();
        BoxOptions.SetShine(p, SlotId.일반2, SlotGroup.일반스킬, false, onlyThis: true, Aden);
        Assert.False(p.Shines(SlotId.일반2, SlotGroup.일반스킬));
        Assert.True(p.Shines(SlotId.일반3, SlotGroup.일반스킬));
    }

    [Fact]
    public void A_group_switch_resets_the_boxes_set_one_by_one()
    {
        var p = new MineProfile();
        BoxOptions.SetPulse(p, SlotId.일반2, SlotGroup.일반스킬, true, onlyThis: true, Aden);
        BoxOptions.SetPulse(p, SlotId.일반5, SlotGroup.일반스킬, false, onlyThis: false, Aden);
        Assert.False(p.Pulses(SlotId.일반2, SlotGroup.일반스킬));
        Assert.Empty(p.SlotPulse);

        BoxOptions.SetEndFlash(p, SlotId.각성기, SlotGroup.각성기, true, onlyThis: false, Aden);
        Assert.True(p.EndFlashes(SlotId.각성기, SlotGroup.각성기));
        BoxOptions.SetShine(p, SlotId.초각스, SlotGroup.초각스, false, onlyThis: false, Aden);
        Assert.Contains(SlotGroup.초각스, p.NoShine);
    }

    [Fact]
    public void A_group_switch_keeps_other_groups_boxes()
    {
        var p = new MineProfile();
        BoxOptions.SetEndFlash(p, SlotId.일반1, SlotGroup.일반스킬, true, onlyThis: true, Aden);
        BoxOptions.SetEndFlash(p, SlotId.아덴1, SlotGroup.아덴스킬, true, onlyThis: false, Aden);
        Assert.True(p.SlotEndFlash[SlotId.일반1]);
    }

    [Fact]
    public void Colouring_a_box_makes_the_class_colours_personal()
    {
        var p = new MineProfile();
        BoxOptions.SetColor(p, SlotId.각성기, SlotGroup.각성기, "#112233", onlyThis: false);
        Assert.Equal(ColorSource.Personal, p.Color);
        Assert.Equal("#112233", p.Colors[SlotGroup.각성기]);

        BoxOptions.SetColor(p, SlotId.일반4, SlotGroup.일반스킬, "#445566", onlyThis: true);
        Assert.False(p.SkillsOneColor);
        Assert.Equal("#445566", p.SlotColors[SlotId.일반4]);
    }

    [Fact]
    public void Colouring_the_whole_일반스킬_group_brings_back_one_colour()
    {
        var p = new MineProfile();
        BoxOptions.SetColor(p, SlotId.일반4, SlotGroup.일반스킬, "#445566", onlyThis: true);
        BoxOptions.SetColor(p, SlotId.일반4, SlotGroup.일반스킬, "#778899", onlyThis: false);
        Assert.True(p.SkillsOneColor);
        Assert.Equal("#778899", p.Colors[SlotGroup.일반스킬]);
    }

    [Fact]
    public void Key_letters_are_per_box()
    {
        var p = new MineProfile();
        BoxOptions.SetKeyLetter(p, SlotId.일반1, true);
        Assert.Contains(SlotId.일반1, p.KeyLetters);
        BoxOptions.SetKeyLetter(p, SlotId.일반1, false);
        Assert.Empty(p.KeyLetters);
    }
}
