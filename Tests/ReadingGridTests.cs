using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>reading-place.plan.md C: the grid lines inside 읽는 자리's boxes, from the same Geometry the reads use.</summary>
public class ReadingGridTests
{
    const int BuffSize = 120, BuffCount = 10, BossSize = 100;

    public static TheoryData<int, int, int, int, double> Layouts => new()
    {
        { 0, 0, 1920, 1080, 110.05 },   // 1080p
        { 0, 0, 3840, 2160, 100 },      // 4K
        { 0, 0, 2560, 1080, 110.05 },   // 21:9
        { 0, 270, 3840, 1620, 80 },     // forced 21:9 inside a 4K screen, small HUD
    };

    static IReadOnlyList<GridLine> Of(string id, Viewport vp, Fit fit, PixelRect? panel = null) =>
        ReadingGrid.Of(id, vp, fit, BuffSize, BuffCount, BossSize, panel);

    [Theory, MemberData(nameof(Layouts))]
    public void Every_line_lies_inside_its_box(int x, int y, int w, int h, double hud)
    {
        var (vp, fit) = (new Viewport(x, y, w, h), new Fit(hud));
        var panel = Geometry.UsualPanel(vp);
        var boxes = new Dictionary<string, PixelRect>
        {
            ["buff"] = Geometry.BuffArea(vp, fit, BuffSize, BuffCount),
            [DetectPlace.DebuffRow] = Geometry.DebuffArea(vp, fit, BuffSize),
            ["amsu"] = Geometry.BossDebuffStrip(vp, fit, BossSize),
            ["boss"] = Geometry.BossTextRow(vp, fit),
            ["g-jeonbun"] = panel,
        };
        foreach (var (id, box) in boxes)
        {
            var lines = Of(id, vp, fit, panel);
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.True(Inside(l, box), $"{id} {l} outside {box}"));
        }
    }

    [Fact]
    public void The_skill_bar_outlines_every_slot()
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110.05));
        Assert.Equal(SlotInfo.All.Length * 4, Of("skill", vp, fit).Count);
    }

    [Fact]
    public void The_buff_and_debuff_rows_frame_each_icon_and_its_timer()
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110.05));
        foreach (var (id, boxes) in new (string, IReadOnlyList<Geometry.BuffBox>)[] { ("buff", Geometry.BuffBoxes(vp, fit, BuffSize, BuffCount)), (DetectPlace.DebuffRow, Geometry.DebuffBoxes(vp, fit, BuffSize)) })
        {
            var lines = Of(id, vp, fit);
            Assert.Equal(boxes.Count * 8, lines.Count);
            foreach (var b in boxes)
            {
                Assert.Contains(new GridLine(b.Icon.X, b.Icon.Y, b.Icon.Right, b.Icon.Y), lines);
                Assert.Contains(new GridLine(b.Timer.X, b.Timer.Bottom, b.Timer.Right, b.Timer.Bottom), lines);
            }
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(110)]
    [InlineData(120)]
    public void The_boss_row_frames_a_slot_per_icon_the_size_shows_from_the_first_debuff(int bossSize)
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110.05));
        var slots = Geometry.BossDebuffSlots(vp, fit, bossSize);
        Assert.Equal(Geometry.MaxIcons(bossSize, 110.05), slots.Count);
        Assert.Equal(Geometry.FirstBossDebuffX(vp, fit), slots[0].Icon.X);
        int side = (int)Math.Round(DebuffReader.BossIconSize * Geometry.BossScale(vp, fit, bossSize));
        Assert.All(slots, s => Assert.Equal((side, side, s.Icon.Bottom), (s.Icon.Width, s.Icon.Height, s.Timer.Y)));
        int buffs = Geometry.BossBuffSlots(vp, fit, bossSize).Count;   // the boss's own buffs left of the slash, icon and timer each
        Assert.Equal((slots.Count + buffs) * 8, ReadingGrid.Of("amsu", vp, fit, BuffSize, BuffCount, bossSize, null).Count);
    }

    // 빛암 120_110_100.mp4 at 70.5 s (HUD 110, boss size 110): icons 28 px wide every 31 px from x 928 (the 예시's crop).
    [Fact]
    public void At_boss_size_110_the_slots_land_on_the_measured_icons()
    {
        var slots = Geometry.BossDebuffSlots(new Viewport(0, 0, 1920, 1080), new Fit(110.05), 110);
        Assert.InRange(slots[0].Icon.Width, 27, 29);
        Assert.InRange(slots[4].Icon.X - slots[0].Icon.X, 4 * 31 - 2, 4 * 31 + 2);
    }

    [Fact]
    public void The_boss_bar_shows_its_ends_and_text_line()
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110.05));
        var bar = Geometry.BossBar(vp, fit);
        var lines = Of("boss", vp, fit);
        Assert.Contains(lines, l => l.X1 == bar.Right && l.X2 == bar.Right);
        Assert.DoesNotContain(lines, l => l.X1 == l.X2 && l.X1 == Geometry.BossTextRow(vp, fit).X);   // an end outside the row isn't drawn on its edge
        Assert.Equal(2, lines.Count(l => l.Y1 == l.Y2));
    }

    [Fact]
    public void The_jeonbun_panel_scales_its_layout_with_the_panel_width()
    {
        var small = Of("g-jeonbun", default, new Fit(110), new PixelRect(100, 50, 470, 114));
        var big = Of("g-jeonbun", default, new Fit(110), new PixelRect(100, 50, 940, 228));
        Assert.Equal(small.Count, big.Count);
        Assert.Contains(small, l => l.X1 == l.X2 && l.X1 == 100 + 118);   // a cell edge at x 118 of 470
        Assert.Contains(big, l => l.X1 == l.X2 && l.X1 == 100 + 236);
    }

    [Fact]
    public void Boxes_without_an_inner_layout_have_no_lines()
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110.05));
        Assert.Empty(Of("dungeon", vp, fit));
        Assert.Empty(Of("char", vp, fit));
        Assert.Empty(Of("judg", vp, fit));
        Assert.Empty(Of("g-jeonbun", vp, fit, null));   // no panel spot: nothing to draw
    }

    static bool Inside(GridLine l, PixelRect r) =>
        Math.Min(l.X1, l.X2) >= r.X && Math.Max(l.X1, l.X2) <= r.Right && Math.Min(l.Y1, l.Y2) >= r.Y && Math.Max(l.Y1, l.Y2) <= r.Bottom;

    [Fact]
    public void A_class_that_draws_Z_and_C_elsewhere_gets_its_slots_framed_there()
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110.05));
        var own = new Dictionary<SlotId, PixelRect> { [SlotId.아덴1] = new(888, 1026, 40, 43) };   // 발키리's round Z (classes.json)
        var z = Geometry.Slot(SlotId.아덴1, vp, fit, own);
        var lines = ReadingGrid.Of("skill", vp, fit, BuffSize, BuffCount, BossSize, null, own);
        Assert.Contains(lines, l => l.X1 == z.X && l.Y1 == z.Y);
        var usual = Geometry.Slot(SlotId.아덴1, vp, fit);
        Assert.DoesNotContain(lines, l => l.X1 == usual.X && l.Y1 == usual.Y);
    }
}
