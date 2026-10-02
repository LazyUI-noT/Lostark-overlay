using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The gold frame a buff draws around some skills (PLAN §3 "Gold frame"), on crops cut from three clips of two
/// characters at HUD 110 and 100 (tools/make-fixtures.ps1).</summary>
public class GoldFrameTests
{
    public static TheoryData<string, bool> Crops => new()
    {
        // 333.mp4 at 4 s: the orange skills lit, the blue S and D not; at 3 s none.
        { "gold_333_4_일반1", true }, { "gold_333_4_일반2", true }, { "gold_333_4_일반3", true }, { "gold_333_4_일반4", true },
        { "gold_333_4_일반8", true }, { "gold_333_4_일반6", false }, { "gold_333_4_일반7", false },
        { "gold_333_3_일반1", false }, { "gold_333_3_일반2", false }, { "gold_333_3_일반3", false }, { "gold_333_3_일반4", false },
        { "gold_333_3_일반6", false }, { "gold_333_3_일반7", false }, { "gold_333_3_일반8", false },
        // live test.mp4 (another character): E and R lit, Q and W not.
        { "gold_lt_1_일반3", true }, { "gold_lt_1_일반4", true }, { "gold_lt_1_일반1", false }, { "gold_lt_1_일반2", false },
        // gameplay.mp4 at HUD 100.
        { "gold_gameplay_26.5_일반1", true }, { "gold_gameplay_26.5_일반2", true }, { "gold_gameplay_26.5_일반3", true }, { "gold_gameplay_26.5_일반4", true },
        { "gold_gameplay_20_일반1", false }, { "gold_gameplay_20_일반2", false }, { "gold_gameplay_20_일반3", false }, { "gold_gameplay_20_일반4", false },
    };

    [Theory]
    [MemberData(nameof(Crops))]
    public void The_gold_frame_is_seen_where_the_game_draws_it(string name, bool lit) =>
        Assert.Equal(lit, SlotClassifier.GoldFrame(Fixture.Load(name).Frames[0]));

    /// <summary>Ready, cooldown, 마나 부족 and 침묵 on the screenshots' character, and another character's clear bar: none
    /// has a gold frame. 각성기's frame is always gold, so it is never checked.</summary>
    [Fact]
    public void No_other_slot_crop_has_a_gold_frame()
    {
        var names = new[] { "Nocd", "cd", "nomana", "silenced" }
            .SelectMany(shot => SlotInfo.All.Where(s => s != SlotId.각성기).Select(s => $"{shot}_{s}"))
            .Concat(Enumerable.Range(1, 8).Select(i => $"lt2_clear_일반{i}"));
        Assert.All(names, n => Assert.False(SlotClassifier.GoldFrame(Fixture.Load(n).Frames[0]), n));
    }

    [Fact]
    public void Only_the_eight_keys_and_초각스_carry_the_gold_frame()
    {
        var digits = new DigitReader(1);
        bool Gold(SlotId slot, string crop) => SlotClassifier.Read(slot, Fixture.Load(crop).Frames[0], TimeSpan.Zero, null, digits).Gold;
        Assert.True(Gold(SlotId.일반1, "gold_333_4_일반1"));
        Assert.True(Gold(SlotId.초각스, "gold_333_4_일반1"));
        Assert.False(Gold(SlotId.일반1, "gold_333_3_일반1"));
        Assert.All(new[] { SlotId.각성기, SlotId.아덴1, SlotId.아덴2 }, s => Assert.False(Gold(s, "gold_333_4_일반1"), s.ToString()));
    }
}
