using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class GeometryTests
{
    [Fact]
    public void Reference_fit_returns_the_measured_rects()
    {
        Assert.Equal(new PixelRect(783, 1021, 50, 49), Geometry.Slot(SlotId.일반7, Fhd, new Fit(110)));
        Assert.Equal(new PixelRect(1009, 964, 46, 55), Geometry.Slot(SlotId.아덴2, Fhd, new Fit(110)));
    }

    /// <summary>A class that draws its 아덴 skills elsewhere (발키리: round Z and C under the orb) gives its own rects;
    /// other slots keep theirs.</summary>
    [Fact]
    public void A_class_can_move_its_aden_slots()
    {
        var own = new Dictionary<SlotId, PixelRect> { [SlotId.아덴1] = new(888, 1026, 40, 43) };
        Assert.Equal(new PixelRect(888, 1026, 40, 43), Geometry.Slot(SlotId.아덴1, Fhd, new Fit(110), own));
        Assert.Equal(new PixelRect(1009, 964, 46, 55), Geometry.Slot(SlotId.아덴2, Fhd, new Fit(110), own));
    }

    // Distance from screen centre to the left edge of the 각성기 gold frame, measured by hand on the
    // 80/90/105/110% screenshots (PLAN.md §1). The frame sits 9 reference px left of the icon.
    [Theory]
    [InlineData(80, 330)]
    [InlineData(90, 372)]
    [InlineData(105, 435)]
    [InlineData(110, 455)]
    public void Hud_scaling_matches_the_screenshots(double hud, int measuredFrameDistance)
    {
        var frame = Geometry.ToScreen(Geometry.RefAwakeningFrame, Fhd, new Fit(hud));
        Assert.InRange(960 - frame.X, measuredFrameDistance - 3, measuredFrameDistance + 3);
    }

    [Fact]
    public void Video_crop_at_hud_100_matches_the_hand_measured_D_slot()
    {
        // Hand-measured in gameplay.mp4: D slot icon at x≈797–841, y≈1025–1069.
        var d = Geometry.Slot(SlotId.일반7, Fhd, new Fit(100));
        Assert.InRange(d.X, 796, 801);
        Assert.InRange(d.Y, 1024, 1028);
    }

    [Fact]
    public void Forced_21_9_scales_by_viewport_height_and_anchors_at_its_bottom_centre()
    {
        // forced_219.png: black bars measured at rows 0–134 and 945–1079 → viewport 1920×810 at y=135.
        // The 각성기 icon was checked visually at (626, 878, 42, 41) in that screenshot.
        var vp = new Viewport(0, 135, 1920, 810);
        var v = Geometry.Slot(SlotId.각성기, vp, new Fit(110));
        Assert.InRange(v.X, 624, 628);
        Assert.InRange(v.Y, 876, 880);
        Assert.InRange(v.Width, 41, 43);
    }

    [Theory]
    [InlineData("probe_219", 135, 810)]   // forced_219.png: bars at rows 0–134 and 945–1079
    [InlineData("probe_Nocd", 0, 1080)]
    public void Letterbox_bars_are_cut_from_the_viewport(string probe, int y, int height)
    {
        var client = new PixelRect(0, 0, 1920, 1080);
        var f = Fixture.Load(probe);
        Assert.Equal(Letterbox.Probe(client), new PixelRect(f.Rect[0], f.Rect[1], f.Rect[2], f.Rect[3]));
        Assert.Equal(new Viewport(0, y, 1920, height), Letterbox.Viewport(client, f.Frames[0]));
    }

    [Fact]
    public void Scale_is_hud_share_times_height_share()
    {
        Assert.Equal(1.0, Geometry.Scale(Fhd, new Fit(110)), 6);
        Assert.Equal(2 * 100 / 110.0, Geometry.Scale(new Viewport(0, 0, 3840, 2160), new Fit(100)), 6);
    }

    [Fact]
    public void Fixture_script_uses_the_same_slot_table_as_Geometry()
    {
        foreach (var slot in SlotInfo.All)
        {
            var f = Fixture.Load($"Nocd_{slot}");
            var r = Geometry.RefSlot(slot);
            Assert.Equal([r.X, r.Y, r.Width, r.Height], f.Rect);
        }
    }
}
