using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class AwakeningLabelTests
{
    private static readonly PixelRect Label = Geometry.RefAwakeningLabel;

    // HUD 110 screenshots with the 각성기 frame (reference 505, 982) at a known spot: 각성기변화.PNG shows the red banner,
    // 각성기변화2.png the grey one with another key binding (gauge full, 각성기 still on cooldown).
    [Theory]
    [InlineData("label_ref", 27, 65)]
    [InlineData("label_ref2", 11, 38)]
    public void Reference_screenshots_show_the_banner(string name, int frameX, int frameY)
    {
        var img = Fixture.Load(name).Frames[0];
        Assert.True(AwakeningLabel.IsShown(VisionReplayTests.Crop(img, Label.X - 505 + frameX, Label.Y - 982 + frameY, Label.Width, Label.Height)));
    }

    [Theory]
    [InlineData("label_gameplay_5", 100, false)]      // no banner
    [InlineData("label_gameplay_5.5", 100, false)]    // dark scenes, the hardest backgrounds for a grey banner
    [InlineData("label_gameplay_23.8", 100, false)]
    [InlineData("label_lt_5.2", 110, false)]
    [InlineData("label_gameplay_14.25", 100, true)]   // grey banner: gauge full, 각성기 still on cooldown
    [InlineData("label_gameplay_15.5", 100, true)]    // red banner
    [InlineData("label_sample_1", 110, true)]         // 각성기 변화_sample.mp4: grey on the dark void
    [InlineData("label_sample_5.5", 110, true)]       // red on the bright floor
    [InlineData("label_sample_9.5", 110, true)]       // grey on the bright floor
    [InlineData("label_sample_10.5", 110, true)]
    public void Gameplay_banner_is_read(string name, double hud, bool shown)
    {
        var f = Fixture.Load(name);
        var r = Geometry.ToScreen(Label, Fhd, new Fit(hud));
        var crop = VisionReplayTests.Crop(f.Frames[0], r.X - f.Rect[0], r.Y - f.Rect[1], r.Width, r.Height);
        Assert.Equal(shown, AwakeningLabel.IsShown(crop));
    }

    // The white flash as 각성기 comes off cooldown hides the banner for ~0.2 s, under one read (250 ms apart).
    [Theory]
    [InlineData(true, false, false, true)]    // seen
    [InlineData(false, true, false, true)]    // first miss while on: held
    [InlineData(false, true, true, false)]    // second miss in a row: off
    [InlineData(false, false, false, false)]  // stays off
    public void One_missed_read_keeps_the_banner_on(bool seen, bool wasOn, bool missedLast, bool on) =>
        Assert.Equal(on, AwakeningLabel.Hold(seen, wasOn, missedLast));
}
