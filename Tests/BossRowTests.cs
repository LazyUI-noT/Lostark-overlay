using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The boss debuff row is found under the boss bar wherever the HUD, 21:9 and the resolution put it (amsu.plan.md Task 1):
/// a search strip a little taller than the expected row, and the row of framed icons found inside it.</summary>
public class BossRowTests
{
    // amsu_*: a friend's 2560×1080 (21:9) game at HUD 100 and 보스 디버프 크기 100; each fixture is the game area from (1000, 100).
    static readonly Viewport Wide = new(0, 0, 2560, 1080);
    static readonly Fit Hud100 = new(100);
    const int FixtureX = 1000, FixtureY = 100, ClipBossSize = 100;
    static readonly PixelRect ClipIcons = new(1250, 152, 222, 24);   // the icons' row in the game area (by eye on the frames)

    [Fact]
    public void The_search_strip_holds_the_21_9_clips_icons_with_room_above_and_below()
    {
        var strip = Geometry.BossRowSearch(Wide, Hud100, ClipBossSize);
        double icon = DebuffReader.BossIconSize * Geometry.BossScale(Wide, Hud100, ClipBossSize);

        Assert.True(strip.X <= ClipIcons.X && strip.Right >= ClipIcons.Right, $"{strip} misses the icons' x");
        Assert.True(ClipIcons.Y - strip.Y >= icon && strip.Bottom - ClipIcons.Bottom >= icon / 2, $"{strip}: too little room round the row");
    }

    [Theory]
    [InlineData("amsu_15")]
    [InlineData("amsu_25")]
    [InlineData("amsu_35")]
    [InlineData("amsu_55")]
    public void The_row_is_found_in_the_21_9_clip(string name)
    {
        var strip = Geometry.BossRowSearch(Wide, Hud100, ClipBossSize);
        var img = Fixture.Load(name).Frames[0].Crop(new PixelRect(strip.X - FixtureX, strip.Y - FixtureY, strip.Width, strip.Height));
        double scale = Geometry.BossScale(Wide, Hud100, ClipBossSize);

        var y = DebuffReader.FindRowY(img, scale);

        Assert.NotNull(y);
        Assert.InRange(strip.Y + y!.Value, ClipIcons.Y - 3, ClipIcons.Y + 3);
    }

    [Fact]
    public void The_row_is_found_where_the_1080p_row_has_it()
    {
        var row = Fixture.Load("bossrow").Frames[0];   // mordum.png: 1920×1080, HUD 110, 보스 디버프 크기 120: the row as BossRow places it
        Assert.InRange(DebuffReader.FindRowY(row, 1.0)!.Value, DebuffReader.BossIconTop - 2, DebuffReader.BossIconTop + 2);
    }

    [Fact]
    public void A_strip_without_framed_icons_has_no_row()
    {
        var grey = new Image(400, 80, Enumerable.Repeat((byte)60, 400 * 80 * 4).ToArray());
        Assert.Null(DebuffReader.FindRowY(grey, 1.0));
    }

    [Fact]
    public void A_line_found_once_elsewhere_doesnt_move_the_settled_row()
    {
        var row = RowLock.None;
        foreach (int y in new[] { 154, 153, 154 }) row = row.Next(y);
        Assert.Equal(154, row.Y);

        row = row.Next(171);   // amsu_45: a damage number over the row pulls one find down
        Assert.Equal(154, row.Y);
        row = row.Next(null);   // nothing found: keep it
        Assert.Equal(154, row.Y);

        foreach (int y in new[] { 120, 121, 120 }) row = row.Next(y);   // the row really moved (another HUD)
        Assert.Equal(120, row.Y);
    }

    [Fact]
    public void Before_anything_settles_there_is_no_row() =>
        Assert.Null(RowLock.None.Next(154).Next(154).Y);

    /// <summary>The 21:9 fixture's strip at reference scale and its row line, as AmsuReader sees them.</summary>
    internal static (Image Row, int Y) RefRow(string name)
    {
        var strip = Geometry.BossRowSearch(Wide, Hud100, ClipBossSize);
        var img = Fixture.Load(name).Frames[0].Crop(new PixelRect(strip.X - FixtureX, strip.Y - FixtureY, strip.Width, strip.Height));
        double scale = Geometry.BossScale(Wide, Hud100, ClipBossSize);
        var r = ImageOps.Resample(img, new PixelRect(0, 0, img.Width, img.Height), 1 / scale);
        return (r, (int)Math.Round(DebuffReader.FindRowY(img, scale)!.Value / scale));
    }

    // The row's top follows the HUD and the resolution only (user and the clips, 2026-11-04): measured from the boss HP bar, the
    // icons' top is at 169–170 on the 프로켈 clips (1920×1080, HUD 110: the bar at y 69–95 as RefBossBar) at every 보스 디버프 크기,
    // and at 154 on the friend's 21:9 clip (2560×1080, HUD 100: the bar 0.909 of it). The strip holds that line and the "N초".
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
        Assert.Equal(Geometry.BossRowSearch(vp, fit, size).X, strip.X);   // the slots (BossSlotsIn) count from the same left edge
    }
}
