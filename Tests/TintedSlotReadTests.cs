using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The dead / disabled tint is read only while the HUD vote says the skill bar is on screen (Engine.ReadSlots), and
/// the vote counts slots that still match their ready reference. So a tinted slot must stay readable against the reference
/// taken before the tint. Fixtures: the same eight slots of the long 종탑 video at 556 s (before a hold) and 559 s (held).</summary>
public class TintedSlotReadTests
{
    // Measured 2026-11-11: 0.86 (일반2, cooling) to 1.00; SlotClassifier calls a slot Unreadable under 0.4.
    const double StillTheSlot = 0.6;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void A_tinted_slot_still_matches_its_ready_reference(int slot)
    {
        var ready = Fixture.Load($"hold556_일반{slot}").Frames[0];
        var tinted = Fixture.Load($"hold559_일반{slot}").Frames[0];

        double similarity = new SlotCalibration(ready).Similarity(tinted);

        Assert.True(similarity >= StillTheSlot, $"일반{slot}: {similarity:0.00}");
    }

    [Fact]
    public void The_bar_before_the_hold_is_not_tinted()
    {
        double[] shares = [.. Enumerable.Range(1, 8).Select(i => SkillTint.Share(Fixture.Load($"hold556_일반{i}").Frames[0]))];

        Assert.False(SkillTint.On(shares));
    }
}
