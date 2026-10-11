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

    [Theory]
    // HUD{80,90,100,105}_buff120.png (1920×1080, buff 120%): box 0's right edge and box 4's (box 6's at HUD 80, where
    // the drift was largest), the last gold or green frame column + 1. Below HUD 100 the game packs the row tighter:
    // pitch 32.43 at 80 and 33.9 at 90 against 35 from 100 up, while the icons keep their size.
    [InlineData(80, 876, 4, 745.5)]
    [InlineData(80, 876, 6, 681)]
    [InlineData(90, 865, 4, 729.5)]
    [InlineData(100, 855, 4, 716)]
    [InlineData(105, 850, 4, 710)]
    public void Boxes_at_each_hud_size_match_the_buff_120_shots(int hud, int right, int box, double boxRight)
    {
        var boxes = Geometry.BuffBoxes(Fhd, new Fit(hud), 120);
        Assert.InRange(boxes[0].Icon.Right, right - 1, right + 1);
        Assert.InRange(boxes[box].Icon.Right, boxRight - 2, boxRight + 2);
        Assert.InRange(boxes[0].Icon.Width, 30, 32);   // the icon follows the buff size, not the HUD
    }

    /// <summary>The row's height follows the HUD too: box 0's frame bottom (the last orange row + 1, a pixel column through its
    /// middle) in HUD{80,90,100,105}_buff120.png and buff_gameplay (110). 1.32 px per HUD %, not the 1.26 a fully HUD-scaled
    /// baseline gives (HUD 80 sat 3 px high).</summary>
    [Theory]
    [InlineData(80, 964)]
    [InlineData(90, 950)]
    [InlineData(100, 937)]
    [InlineData(105, 931)]
    [InlineData(110, 924)]
    public void Box_bottoms_at_each_hud_size_match_the_buff_120_shots(int hud, int bottom) =>
        Assert.InRange(Geometry.BuffBoxes(Fhd, new Fit(hud), 120)[0].Icon.Bottom, bottom - 1, bottom + 1);

    /// <summary>The debug window's guide lines over a row: a column edge between every two boxes and at both ends (the timer
    /// cells tile the row), and the line between the icons and their timers.</summary>
    [Fact]
    public void The_row_grid_separates_every_box_and_the_icons_from_the_timers()
    {
        var boxes = Geometry.BuffBoxes(Fhd, new Fit(110), 120);
        var (columns, iconBottom) = Geometry.RowGrid(boxes);
        Assert.Equal(Geometry.BuffBoxCount + 1, columns.Length);
        Assert.Equal(columns.Order(), columns);
        Assert.All(columns.Zip(columns.Skip(1)), p => Assert.InRange(p.Second - p.First, 34, 36));   // pitch 35 at buff 120
        Assert.Equal(boxes[0].Icon.Bottom, iconBottom);
        Assert.All(boxes, b => Assert.Contains(columns, c => c <= b.Icon.X));
        Assert.Equal(Geometry.DebuffBoxCount + 1, Geometry.RowGrid(Geometry.DebuffBoxes(Fhd, new Fit(110), 120)).Columns.Length);
    }

    [Fact]
    public void The_debuff_row_packs_like_the_buff_row()
    {
        var buffs = Geometry.BuffBoxes(Fhd, new Fit(80), 120);
        var debuffs = Geometry.DebuffBoxes(Fhd, new Fit(80), 120);
        Assert.InRange(debuffs[4].Icon.X - debuffs[0].Icon.X, buffs[0].Icon.X - buffs[4].Icon.X - 1, buffs[0].Icon.X - buffs[4].Icon.X + 1);
    }

    [Theory]
    // The boss's effects under its HP bar: their row's top follows the HUD from the top of the screen (170 at HUD 110 in
    // mordum.png, 122.6 at 80 in HUD80_buff120.png, ~155 at 100 in bossHP_hud100.png), while the icons keep their size like
    // the buff row's (31.7 px at 110, 29.4 at 80).
    [InlineData(110, 170)]
    [InlineData(100, 155)]
    [InlineData(80, 122.6)]
    public void The_boss_row_puts_the_sigils_where_the_boss_effects_are(int hud, double iconTop)
    {
        var row = Geometry.BossRow(Fhd, new Fit(hud));
        double s = Geometry.BossScale(Fhd, new Fit(hud));
        Assert.InRange(row.Y + DebuffReader.BossIconTop * s, iconTop - 2.5, iconTop + 2.5);
        Assert.InRange(row.Bottom, iconTop + 32 * s, 1080);   // the whole icon row is captured
        // HUD80_buff120.png, estimated from the boss debuffs there: the sigils at x 862–924.
        Assert.True(row.X <= 862 && row.Right >= 924, $"{row}");
    }

    /// <summary>수동 조정's row shifts (screen px): each moves its own row only, sizes unchanged.</summary>
    [Fact]
    public void Row_shifts_move_only_their_own_row_and_keep_sizes()
    {
        var plain = new Fit(110);
        var shifted = plain with { BuffShift = new(4, -3), DebuffShift = new(-2, 5), BossShift = new(7, 1) };
        static PixelRect By(PixelRect r, int dx, int dy) => r with { X = r.X + dx, Y = r.Y + dy };
        var (b0, b1) = (Geometry.BuffBoxes(Fhd, plain, 120), Geometry.BuffBoxes(Fhd, shifted, 120));
        var (d0, d1) = (Geometry.DebuffBoxes(Fhd, plain, 120), Geometry.DebuffBoxes(Fhd, shifted, 120));
        for (int i = 0; i < b0.Length; i++)
            Assert.Equal((By(b0[i].Icon, 4, -3), By(b0[i].Timer, 4, -3)), (b1[i].Icon, b1[i].Timer));
        for (int i = 0; i < d0.Length; i++)
            Assert.Equal((By(d0[i].Icon, -2, 5), By(d0[i].Timer, -2, 5)), (d1[i].Icon, d1[i].Timer));
        Assert.Equal(By(Geometry.BossRow(Fhd, plain), 7, 1), Geometry.BossRow(Fhd, shifted));
        Assert.Equal(By(Geometry.BuffArea(Fhd, plain, 120), 4, -3), Geometry.BuffArea(Fhd, shifted, 120));
    }

    [Fact]
    public void The_boss_row_at_HUD_110_is_the_reference_strip()
    {
        Assert.Equal(new PixelRect(700, 140, 540, 70), Geometry.BossRow(Fhd, new Fit(110)));
        Assert.Equal(1, Geometry.BossScale(Fhd, new Fit(110)));
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
