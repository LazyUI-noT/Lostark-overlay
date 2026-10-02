using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class CooldownLookTests
{
    static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);
    static SlotState Cooling(bool stacked = false, bool stackSkill = false) =>
        new(SlotId.일반3, SlotPhase.Cooldown, T0 + TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), false, T0,
            Stacked: stacked, StackSkill: stackSkill || stacked);

    [Theory]
    [InlineData(CooldownStyle.Sweep)]
    [InlineData(CooldownStyle.Fill)]
    public void An_ordinary_cooldown_is_dim_and_refills_one_step_brighter(CooldownStyle style) =>
        Assert.Equal(new CooldownLook(CooldownLook.CooldownLevel, false, CooldownLook.SweepLevel, false), CooldownLook.For(Cooling(), style));

    /// <summary>Slashes on the refilled part stay until Ready, so an almost-full box never passes for a ready one.</summary>
    [Fact]
    public void The_slashed_fill_refills_in_full_colour_with_slashes() =>
        Assert.Equal(new CooldownLook(CooldownLook.CooldownLevel, false, 1, true), CooldownLook.For(Cooling(), CooldownStyle.FillHatched));

    /// <summary>A use left: the skill can be cast now, so the box is as bright as a ready one and only a white line
    /// shows the recharge.</summary>
    [Theory]
    [InlineData(CooldownStyle.Sweep)]
    [InlineData(CooldownStyle.Fill)]
    [InlineData(CooldownStyle.FillHatched)]
    public void A_stack_skill_with_a_use_left_is_full_colour_with_a_white_line(CooldownStyle style)
    {
        var look = CooldownLook.For(Cooling(stacked: true), style);
        Assert.Equal(new CooldownLook(1, false, null, false), look);
        Assert.True(look.Line);
    }

    [Fact]
    public void A_stack_skill_with_no_use_left_is_grey_and_refills_in_its_dimmed_colour()
    {
        Assert.Equal(new CooldownLook(CooldownLook.CooldownLevel, true, CooldownLook.SweepLevel, false),
            CooldownLook.For(Cooling(stackSkill: true), CooldownStyle.Sweep));
        Assert.Equal(new CooldownLook(CooldownLook.CooldownLevel, true, 1, true),
            CooldownLook.For(Cooling(stackSkill: true), CooldownStyle.FillHatched));
    }

    [Theory]
    [InlineData(FillFrom.Bottom, 0, 75, 100, 25)]
    [InlineData(FillFrom.Top, 0, 0, 100, 25)]
    [InlineData(FillFrom.Left, 0, 0, 25, 100)]
    [InlineData(FillFrom.Right, 75, 0, 25, 100)]
    public void The_fill_grows_from_its_edge(FillFrom from, double x, double y, double w, double h) =>
        Assert.Equal((x, y, w, h), CooldownLook.FillRect(0, 0, 100, 100, 0.25, from));

    [Fact]
    public void The_fill_is_clamped_to_the_box() =>
        Assert.Equal((10.0, 20.0, 30.0, 40.0), CooldownLook.FillRect(10, 20, 30, 40, 1.5, FillFrom.Bottom));
}
