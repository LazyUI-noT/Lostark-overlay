using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The loading screen's bar (meter-gating.plan.md phase 2). Fixtures load_*: the bar strip
/// (Geometry.LoadingBar) once a second from the start of each clip; the loading seconds are by eye on the frames.</summary>
public class LoadingScreenTests
{
    [Theory]
    [InlineData("load_wide1", new[] { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 })]   // 2560×1080 (21:9) in a 1440 recording
    [InlineData("load_wide2", new[] { 3, 4, 5, 6 })]
    [InlineData("load_v4", new[] { 7, 8, 9, 10 })]                            // 1920×1080
    [InlineData("load_hell", new[] { 0, 2, 3, 4 })]                           // 1 s: the Windows taskbar covers the bar
    public void Only_the_loading_seconds_show_the_bar(string name, int[] loading)
    {
        var fx = Fixture.Load(name);
        var seen = fx.Frames.Select((f, i) => (f, i)).Where(p => LoadingScreen.Is(p.f, 1)).Select(p => p.i).ToArray();
        Assert.Equal(loading, seen);
    }

    [Fact]
    public void The_bar_is_found_at_a_third_more_size()
    {
        var frames = Fixture.Load("load_v4").Frames;
        var all = new PixelRect(0, 0, frames[0].Width, frames[0].Height);
        Assert.True(LoadingScreen.Is(ImageOps.Resample(frames[8], all, 4 / 3.0), 4 / 3.0));    // 1440p 16:9
        Assert.False(LoadingScreen.Is(ImageOps.Resample(frames[2], all, 4 / 3.0), 4 / 3.0));
        Assert.True(LoadingScreen.Is(ImageOps.Resample(frames[8], all, 2), 2));                // 4K
    }

    [Fact]
    public void A_black_or_empty_strip_is_no_loading_screen()
    {
        Assert.False(LoadingScreen.Is(new Image(1820, 32), 1));
        Assert.False(LoadingScreen.Is(new Image(0, 0), 1));
    }

    // The bar sits in the game area's centred 16:9 part (a forced 21:9 game shows the 16:9 loading art in its middle) and
    // follows the height only; HUD 크기 doesn't move it.
    [Theory]
    [InlineData(0, 0, 1920, 1080, 45, 1018, 1820, 32)]
    [InlineData(0, 0, 2560, 1080, 365, 1018, 1820, 32)]    // the 21:9 clips: track from x 377
    [InlineData(0, 0, 2560, 1440, 60, 1357, 2427, 43)]
    [InlineData(0, 180, 2560, 1080, 365, 1198, 1820, 32)]  // letterboxed: from the game area's top
    public void The_strip_follows_the_16_9_middle_and_the_height(int x, int y, int w, int h, int rx, int ry, int rw, int rh) =>
        Assert.Equal(new PixelRect(rx, ry, rw, rh), Geometry.LoadingBar(new Viewport(x, y, w, h)));
}
