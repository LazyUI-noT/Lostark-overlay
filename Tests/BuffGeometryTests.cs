using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The buff row (PLAN §4 버프): its bottom-right corner follows the HUD like the skill bar, while the icons
/// scale with the in-game buff size × viewport height. Expected values measured by hand on the recordings.</summary>
public class BuffGeometryTests
{
    [Fact]
    public void Boxes_at_HUD_110_buff_120_match_buff_gameplay()
    {
        // buff_gameplay.mp4 (1920×1080, HUD 110, buff 120%): box 0's gold frame 814.5–845 × 892.5–923.5, pitch 35,
        // timer text 930–941.
        var boxes = Geometry.BuffBoxes(Fhd, new Fit(110), 120);
        Assert.Equal(Geometry.BuffBoxCount, boxes.Length);
        AssertNear(new PixelRect(814, 893, 31, 31), boxes[0].Icon, 1);
        AssertNear(new PixelRect(814 - 4 * 35, 893, 31, 31), boxes[4].Icon, 1);
        Assert.InRange(boxes[0].Timer.Y, 928, 931);
        Assert.InRange(boxes[0].Timer.Bottom, 941, 944);
        Assert.True(boxes[0].Timer.X <= boxes[0].Icon.X && boxes[0].Timer.Right >= boxes[0].Icon.Right);
    }

    [Theory]
    // buff_80_to_110.mp4 (1920×1080, HUD 110, the buff size stepped 80 → 110): box 0's right and bottom edges and box
    // 4's right edge, where the four party buffs score best over each size's frames (integer px, ±1). 120 is
    // buff_gameplay.mp4. The pitch is not proportional to the size: 22.25, 25.5, 28.75, 32, 35.
    [InlineData(80, 846, 930, 757)]
    [InlineData(90, 845, 928, 743)]
    [InlineData(100, 845, 926, 730)]
    [InlineData(110, 845, 925, 717)]
    [InlineData(120, 845, 924, 705)]
    public void Boxes_at_each_buff_size_match_buff_80_to_110(int buffSize, int right, int bottom, int box4Right)
    {
        var boxes = Geometry.BuffBoxes(Fhd, new Fit(110), buffSize);
        Assert.InRange(boxes[0].Icon.Right, right - 1, right + 1);
        Assert.InRange(boxes[0].Icon.Bottom, bottom - 1, bottom + 1);
        Assert.InRange(boxes[4].Icon.Right, box4Right - 1, box4Right + 1);
    }

    [Fact]
    public void Boxes_in_the_scaled_down_4k_recording_match_4k_down()
    {
        // 4k_down.mp4 (viewport 0,135,1920,810, HUD 100, buff 100%): box 0's right edge 880–881, bottom 839, pitch
        // 21.86 over 14 boxes.
        var boxes = Geometry.BuffBoxes(new Viewport(0, 135, 1920, 810), new Fit(100), 100);
        Assert.InRange(boxes[0].Icon.Right, 879, 882);
        Assert.InRange(boxes[0].Icon.Bottom, 838, 840);
        Assert.InRange(boxes[0].Icon.X - boxes[4].Icon.X, 86, 89);   // 4 pitches of 21.86
    }

    [Fact]
    public void Box_0_in_the_1440p_shot_matches_its_food_buff()
    {
        // 1440p_hud100_120buff.png: the game at 1080,162, 2560×1440, HUD 100, buff 120%. The one icon is a food buff
        // (not tracked, but the same size as any buff): green frame 2179–2219 × 1370–1410.
        var box = Geometry.BuffBoxes(new Viewport(1080, 162, 2560, 1440), new Fit(100), 120)[0];
        AssertNear(new PixelRect(2179, 1370, 41, 41), box.Icon, 1);
    }

    /// <summary>Seven buffs are read (the two leftmost past the four party buffs and one more); the debuff row keeps five.</summary>
    [Fact]
    public void Seven_buff_boxes_at_one_pitch_and_five_debuff_boxes()
    {
        var boxes = Geometry.BuffBoxes(Fhd, new Fit(110), 120);
        Assert.Equal(7, boxes.Length);
        Assert.InRange(boxes[5].Icon.X - boxes[6].Icon.X, boxes[0].Icon.X - boxes[1].Icon.X - 1, boxes[0].Icon.X - boxes[1].Icon.X + 1);
        Assert.Equal(boxes[0].Icon.Y, boxes[6].Icon.Y);
        Assert.Equal(5, Geometry.DebuffBoxes(Fhd, new Fit(110), 120).Length);
    }

    [Fact]
    public void Area_covers_every_box_and_timer()
    {
        var boxes = Geometry.BuffBoxes(Fhd, new Fit(110), 120);
        var area = Geometry.BuffArea(Fhd, new Fit(110), 120);
        foreach (var b in boxes)
            foreach (var r in new[] { b.Icon, b.Timer })
                Assert.True(r.X >= area.X && r.Y >= area.Y && r.Right <= area.Right && r.Bottom <= area.Bottom, $"{r} in {area}");
    }

    static void AssertNear(PixelRect want, PixelRect got, int tolerance) =>
        Assert.True(Math.Abs(want.X - got.X) <= tolerance && Math.Abs(want.Y - got.Y) <= tolerance
            && Math.Abs(want.Width - got.Width) <= tolerance && Math.Abs(want.Height - got.Height) <= tolerance, $"{got}, measured {want}");
}
