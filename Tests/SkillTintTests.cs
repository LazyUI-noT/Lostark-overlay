using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The dead / disabled tint on the 일반 slots (dead-disabled.plan.md), all at 1080p HUD 110. Tinted: a town's greyed bar
/// (greyed out.png, dead.PNG on another character), 침묵 (silenced.png), held in 종탑 with two skills cooling (the long video at
/// 559 s), dead in a raid (dead2.PNG, a row of the bar). Not tinted: ready, cooling, 마나 부족, another character, after a clear.</summary>
public class SkillTintTests
{
    static double[] Shares(string family) =>
        [.. Enumerable.Range(1, 8).Select(i => SkillTint.Share(Fixture.Load($"{family}_일반{i}").Frames[0]))];

    // Measured: the lowest tinted slot is 0.58 (dead.PNG 일반3), a cooling tinted one 0.62 (hold559 일반2).
    [Theory]
    [InlineData("greyed")]
    [InlineData("silenced")]
    [InlineData("dead")]
    [InlineData("hold559")]
    public void A_dead_or_disabled_bar_is_tinted_on_every_slot(string family)
    {
        var shares = Shares(family);
        Assert.All(shares, s => Assert.True(s > 0.55, $"{family}: {s:0.00}"));
        Assert.True(SkillTint.On(shares));
    }

    // Measured: 0.04 at most.
    [Theory]
    [InlineData("Nocd")]
    [InlineData("cd")]
    [InlineData("nomana")]
    [InlineData("other")]
    [InlineData("lt2_clear")]
    public void A_bar_in_use_is_not_tinted_on_any_slot(string family)
    {
        var shares = Shares(family);
        Assert.All(shares, s => Assert.True(s < 0.1, $"{family}: {s:0.00}"));
        Assert.False(SkillTint.On(shares));
    }

    // Measured: 0.19 at most (333_E, the cast flash).
    [Theory]
    [InlineData("gameplay_D_end")]
    [InlineData("gameplay_S_end")]
    [InlineData("gameplay_D_start")]
    [InlineData("333_E")]
    public void A_slot_cooling_down_or_flashing_is_never_tinted(string fixture)
    {
        var frames = Fixture.Load(fixture).Frames;
        for (int i = 0; i < frames.Count; i++)
            Assert.True(SkillTint.Share(frames[i]) < 0.25, $"{fixture} frame {i}: {SkillTint.Share(frames[i]):0.00}");
    }

    [Fact]
    public void Dead_in_a_raid_is_tinted() => Assert.True(SkillTint.Share(Fixture.Load("dead2_row").Frames[0]) > 0.55);

    [Fact]
    public void A_crop_with_nothing_to_judge_is_unsure()
    {
        Assert.True(double.IsNaN(SkillTint.Share(new(8, 8))));   // all black: the cooldown sweep
        Assert.True(double.IsNaN(SkillTint.Share(new(0, 0))));
    }

    [Fact]
    public void The_bar_is_tinted_from_six_slots_and_unread_slots_count_as_not()
    {
        Assert.True(SkillTint.On([0.9, 0.9, 0.9, 0.9, 0.9, 0.9, double.NaN, 0.1]));
        Assert.False(SkillTint.On([0.9, 0.9, 0.9, 0.9, 0.9, double.NaN, double.NaN, 0.1]));
        Assert.False(SkillTint.On([]));
    }
}
