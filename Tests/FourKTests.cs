using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>4K native and 4K forced 21:9 (app-wide, PLAN §1a), from the 바드 references at in-game HUD 100% and buff
/// size 100%. Expected edges are the 각성기 gold frame's outer edge, measured by hand on the source images (the slot
/// rects start at the icon's dark border, about 3 reference px outside the art, so the art's edge is not the measure).</summary>
public class FourKTests
{
    static readonly Viewport Uhd219 = new(0, 270, 3840, 1620);   // bars 270 px
    static readonly Viewport Down = new(0, 135, 1920, 810);      // 4k_down.mp4: 4K 21:9 scaled to 1080p

    [Theory]
    // ±1 on the sharp 4K stills: the gold frame edge is a crisp 2–3 px line there, measured on a 3× zoom; the video is
    // blurred over about 2 px.
    [InlineData("band_4k", 0, 3840, 2160, 1092, 1982, 1)]      // 4k_screenshot_bard.jpg
    [InlineData("band_4k219", 270, 3840, 1620, 1300, 1757, 1)] // 4k_21_9_screenshot_bard.png
    [InlineData("band_down", 135, 1920, 810, 651, 876, 2)]     // 4k_down.mp4 at 30 s (blurry: ±2)
    // 1440p_hud100_120buff.png: the game at 1080,162 on the second monitor. The frame's top edge is under the blue
    // "징표 교환" text (gold starts at 1480–1483), so ±2.
    [InlineData("band_1440", 162, 2560, 1440, 1808, 1482, 2, 1080)]
    public void Fits_HUD_100_and_lands_on_the_awakening_frame(string band, int y, int w, int h, int frameX, int frameY, int tolerance, int x = 0)
    {
        var vp = new Viewport(x, y, w, h);
        var f = Fixture.Load(band);
        var fit = FitSolver.Solve(f.Frames[0], new PixelRect(f.Rect[0], f.Rect[1], f.Rect[2], f.Rect[3]), vp);
        Assert.NotNull(fit);
        Assert.InRange(fit.Hud, 99, 101);
        var frame = Geometry.ToScreen(Geometry.RefAwakeningFrame, vp, fit);
        Assert.InRange(frame.X, frameX - tolerance, frameX + tolerance);
        Assert.InRange(frame.Y, frameY - tolerance, frameY + tolerance);
    }

    [Theory]
    [InlineData("down_4s_일반1", "일반1", 4)]
    [InlineData("down_10s_일반3", "일반3", 10)]
    [InlineData("down_22s_초각스", "초각스", 22)]
    [InlineData("down_53s_각성기", "각성기", 53)]
    public void Reads_digits_at_the_scaled_down_4k_size(string name, string slot, int seconds)
    {
        var digits = new DigitReader(Geometry.Scale(Down, new Fit(100)));
        Assert.Equal((ReadKind.Seconds, seconds), digits.Read(Fixture.Load(name).Frames[0], Enum.Parse<SlotId>(slot)));
    }

    [Fact]
    public void Letterbox_finds_the_4k_21_9_bars()
    {
        var f = Fixture.Load("probe_4k219");
        var client = new PixelRect(0, 0, 3840, 2160);
        Assert.Equal(Letterbox.Probe(client), new PixelRect(f.Rect[0], f.Rect[1], f.Rect[2], f.Rect[3]));
        Assert.Equal(Uhd219, Letterbox.Viewport(client, f.Frames[0]));
    }
}
