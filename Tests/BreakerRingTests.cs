using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>브레이커's ring (breaker-aden.plan.md phase 4): left half yellow 기력, right half green 충격, each filling bottom → top,
/// read as gauges from Profiles/aden/브레이커_ring_left and _right (masks: the ring band 45–62 px round the orb's centre
/// 958,1001, cut from the --build-gauge masks of empty_yellow.png / empty_Green.png). Fixtures: tools/make-fixtures.ps1 ($ring).</summary>
public class BreakerRingTests
{
    const int OriginX = 900, OriginY = 945;

    static GaugeDef Def(string side) => GaugeLoader.Parse(
        File.ReadAllText(Path.Combine(Helpers.RepoRoot(), "Profiles", "aden", $"브레이커_ring_{side}", GaugeLoader.JsonFile)),
        Fixture.Load($"gauge_breaker_ring_{side}_mask").Frames[0]);

    static double Fill(string side, string name, int frame = 0)
    {
        var def = Def(side);
        var r = def.Ref;
        return def.Read(Fixture.Load(name).Frames[frame].Crop(new PixelRect(r.X - OriginX, r.Y - OriginY, r.Width, r.Height)));
    }

    [Fact]
    public void Each_half_reads_empty_and_full_on_the_stills()
    {
        Assert.Equal(0, Fill("left", "breaker_ring_empty_yellow"), 0.02);
        Assert.Equal(1, Fill("right", "breaker_ring_empty_yellow"), 0.02);
        Assert.Equal(1, Fill("left", "breaker_ring_empty_green"), 0.02);
        Assert.Equal(0, Fill("right", "breaker_ring_empty_green"), 0.02);
    }

    /// <summary>Frames checked by eye (frame index in the fixture, the fill seen). Left out: 수라act 0.8 s (a shine band across the
    /// ring reads the left 0.42), 권왕 3.0 s (Z's white flash) and 25 s (effects tint the yellow orange, 0.43): single frames
    /// that GaugeFilter's 5 agreeing reads ride over (JeonbunSweep GAUGE_ADEN with the ring folders, 2026-11-14).</summary>
    [Theory]
    [InlineData("breaker_ring_kw", 0, 1.00, 0.17)]    // 0 s: 기력 full, 충격 a short stub at the bottom
    [InlineData("breaker_ring_kw", 3, 1.00, 0.00)]    // 9.2 s: 충격 empty
    [InlineData("breaker_ring_kw", 4, 1.00, 1.00)]    // 12 s
    [InlineData("breaker_ring_act", 2, 0.76, 1.00)]   // 3 s: 기력 up to the top-left bend
    [InlineData("breaker_ring_act", 8, 1.00, 1.00)]   // 12 s
    public void Clip_frames_read_as_seen(string name, int frame, double left, double right)
    {
        Assert.Equal(left, Fill("left", name, frame), 0.05);
        Assert.Equal(right, Fill("right", name, frame), 0.05);
    }

    [Fact]
    public void Ring_bar_fills_each_half_by_its_share_and_clamps()
    {
        Assert.Equal((50, 0), RingSettings.Fills(new RingState(0.5, 0), 100));
        Assert.Equal((100, 0), RingSettings.Fills(new RingState(1.4, -0.2), 100));
    }

    [Fact]
    public void Ring_bar_is_as_long_as_the_gauge_window_and_six_dips_thick_at_scale_one()
    {
        var size = RingSettings.Size(new RingSettings());
        Assert.Equal((SplitLayout.GaugeLength(1), RingSettings.Thickness), size);
        Assert.Equal(12, RingSettings.Size(new RingSettings { Height = 2 }).H);
    }
}
