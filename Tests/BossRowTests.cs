using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The boss debuff row under the boss bar: its top is set by HUD 크기 and the resolution alone (user and the clips,
/// 2026-11-03), so the 암수 strip (Geometry.BossDebuffStrip) is placed, not searched for, and snapped once ±2 px to the frames.</summary>
public class BossRowTests
{
    // amsu_*: a friend's 2560×1080 (21:9) game at HUD 100 and 보스 디버프 크기 100; each fixture is the game area from (1000, 100).
    static readonly Viewport Wide = new(0, 0, 2560, 1080);
    static readonly Fit Hud100 = new(100);
    const int FixtureX = 1000, FixtureY = 100, ClipBossSize = 100;
    static readonly PixelRect ClipIcons = new(1250, 152, 222, 24);   // the icons' row in the game area (by eye on the frames)

    [Fact]
    public void The_strip_spans_the_21_9_clips_icons()
    {
        var strip = Geometry.BossDebuffStrip(Wide, Hud100, ClipBossSize);
        Assert.True(strip.X <= ClipIcons.X && strip.Right >= ClipIcons.Right, $"{strip} misses the icons' x");
        Assert.InRange(Geometry.FirstBossDebuffX(Wide, Hud100), ClipIcons.X - 2, ClipIcons.X + 2);   // the walk starts on the first icon
    }

    [Theory]
    [InlineData("amsu_15")]
    [InlineData("amsu_25")]
    [InlineData("amsu_35")]
    [InlineData("amsu_55")]
    public void The_row_is_where_the_21_9_clip_has_it(string name)
    {
        var strip = Geometry.BossDebuffStrip(Wide, Hud100, ClipBossSize);
        var img = Fixture.Load(name).Frames[0].Crop(new PixelRect(strip.X - FixtureX, strip.Y - FixtureY, strip.Width, strip.Height));
        double scale = Geometry.BossScale(Wide, Hud100, ClipBossSize);
        var snapped = AmsuReader.SnapIn(DebuffReader.ToReference(img, scale), scale, Geometry.BossSlotsIn(Wide, Hud100, ClipBossSize),
            Geometry.BossDebuffLine, Geometry.BossLineSnap);
        Assert.NotNull(snapped);   // the debuffs' frames on the placed line, within the snap
        Assert.InRange(strip.Y + snapped!.Value, ClipIcons.Y - 3, ClipIcons.Y + 3);
    }

    // Measured from the boss HP bar: the icons' top is at 169–170 on the 프로켈 clips (1920×1080, HUD 110: the bar at y 69–95 as
    // RefBossBar) at every 보스 디버프 크기, and at 154 on the friend's 21:9 clip (2560×1080, HUD 100: the bar 0.909 of it). The strip
    // holds that line and the "N초".
    [Theory]
    [InlineData(1920, 1080, 110, 80, 169.5)]
    [InlineData(1920, 1080, 110, 100, 169.5)]
    [InlineData(1920, 1080, 110, 120, 169.5)]
    [InlineData(2560, 1080, 100, 100, 154)]
    public void The_debuff_strip_puts_its_line_on_the_icons_top(int w, int h, double hud, int size, double iconTop)
    {
        var vp = new Viewport(0, 0, w, h);
        var fit = new Fit(hud);
        var strip = Geometry.BossDebuffStrip(vp, fit, size);
        Assert.InRange(strip.Y + Geometry.BossDebuffLine, iconTop - 1.5, iconTop + 1.5);
        double icon = DebuffReader.BossIconSize * Geometry.BossScale(vp, fit, size);
        Assert.True(strip.Bottom >= iconTop + icon + 14 * Geometry.BuffScale(vp, size), $"{strip}: the \"N초\" under the icons is cut");
        Assert.InRange(Geometry.BossSlotsIn(vp, fit, size).FirstX, 0, strip.Width);   // the first slot inside the strip
    }
}
