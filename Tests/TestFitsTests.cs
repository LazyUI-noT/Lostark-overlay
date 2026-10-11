using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>테스트 모드's fits: found on the video in front and kept in memory only, so a video at another resolution or HUD
/// size never uses (or overwrites) the game's own saved fit.</summary>
public class TestFitsTests
{
    static readonly Viewport Wide = new(0, 135, 1920, 810);

    [Fact]
    public void A_fit_is_kept_per_game_area_until_cleared()
    {
        var fits = new TestFits();
        Assert.Null(fits.For(Fhd));
        fits.Set(Fhd, new Fit(80));
        Assert.Equal(new Fit(80), fits.For(Fhd));
        Assert.Null(fits.For(Wide));
        fits.Clear();
        Assert.Null(fits.For(Fhd));
    }

    [Fact]
    public void A_fit_is_tried_at_once_then_every_two_seconds()
    {
        var fits = new TestFits();
        Assert.True(fits.ShouldTry(Fhd, S(10)));
        Assert.False(fits.ShouldTry(Fhd, S(11)));
        Assert.True(fits.ShouldTry(Fhd, S(12)));
    }

    [Fact]
    public void Another_game_area_or_a_clear_is_tried_at_once()
    {
        var fits = new TestFits();
        Assert.True(fits.ShouldTry(Fhd, S(10)));
        Assert.True(fits.ShouldTry(Wide, S(10.5)));   // the video went to 21:9
        fits.Clear();
        Assert.True(fits.ShouldTry(Wide, S(10.6)));   // test mode switched off and on
    }

    [Fact]
    public void Two_areas_flickering_are_each_tried_every_two_seconds()
    {
        var fits = new TestFits();
        Assert.True(fits.ShouldTry(Fhd, S(10)));
        Assert.True(fits.ShouldTry(Wide, S(10.2)));
        Assert.False(fits.ShouldTry(Fhd, S(10.4)));    // back to the first: tried 0.4 s ago
        Assert.False(fits.ShouldTry(Wide, S(10.6)));
        Assert.True(fits.ShouldTry(Fhd, S(12)));
    }

    [Fact]
    public void An_area_with_a_fit_is_not_tried_again()
    {
        var fits = new TestFits();
        fits.Set(Fhd, new Fit(100));
        Assert.False(fits.ShouldTry(Fhd, S(100)));
    }
}
