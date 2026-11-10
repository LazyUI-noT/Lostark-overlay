using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>DebuffReader.FrameMap (icon-gates.plan.md, 1a): every window's frame shares exactly as FrameColours counts them.</summary>
public class FrameMapTests
{
    static readonly Viewport Wide = new(0, 0, 2560, 1080);
    static readonly Fit Hud100 = new(100);

    static Image Strip(string name)
    {
        var s = Geometry.BossDebuffStrip(Wide, Hud100, 100);
        var strip = Fixture.Load(name).Frames[0].Crop(new PixelRect(s.X - 1000, s.Y - 100, s.Width, s.Height));
        return DebuffReader.ToReference(strip, Geometry.BossScale(Wide, Hud100, 100));
    }

    [Theory]
    [InlineData("amsu_15")]
    [InlineData("amsu_35")]
    [InlineData("amsu_55")]
    public void Every_window_the_walk_asks_for_counts_as_FrameColours(string fixture)
    {
        var r = Strip(fixture);
        var map = new DebuffReader.FrameMap(r);
        int icon = DebuffReader.BossIconSize, compared = 0, red = 0;
        for (int y = -2; y < r.Height; y += 3)
            for (int x = -3; x < r.Width + 2; x++)
                foreach (var rect in new[] { new PixelRect(x, y, icon, icon), new PixelRect(x, y + 2, 1, icon - 4), new PixelRect(x, y, 1, 1) })
                {
                    var expected = DebuffReader.FrameColours(r, rect);
                    Assert.Equal(expected, map.Colours(rect));
                    compared++;
                    if (expected.Red > 0) red++;
                }
        Assert.True(red > 0, $"no red frame among {compared} windows: the fixture was not on the boss row");
    }
}
