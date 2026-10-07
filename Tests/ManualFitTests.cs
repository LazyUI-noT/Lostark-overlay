using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>직접 입력 (HUD · 버프 · 해상도 · 21:9): 스킬창 맞추기 takes the game area and the HUD size from the user, and only checks
/// that the 각성기 frame and the HP bar are where they put them, searching wider when they aren't.</summary>
public class ManualFitTests
{
    /// <summary>The game's forced 21:9 is 64:27 (1920×810, 3840×1620: band_219, band_4k219); on a screen as wide or wider it
    /// follows the screen. A window of another size than the typed 해상도 is the game stretched to it.</summary>
    [Theory]
    [InlineData(0, 0, 1920, 1080, 1920, 1080, false, 0, 0, 1920, 1080)]
    [InlineData(0, 0, 1920, 1080, 1920, 1080, true, 0, 135, 1920, 810)]
    [InlineData(0, 0, 3840, 2160, 3840, 2160, true, 0, 270, 3840, 1620)]
    [InlineData(0, 0, 2560, 1440, 2560, 1440, true, 0, 180, 2560, 1080)]
    [InlineData(0, 0, 3440, 1440, 3440, 1440, true, 0, 0, 3440, 1440)]    // wider than 21:9: the screen
    [InlineData(0, 0, 2560, 1600, 2560, 1600, false, 0, 0, 2560, 1600)]   // 16:10 as typed
    [InlineData(0, 0, 2560, 1440, 1920, 1080, true, 0, 180, 2560, 1080)]  // typed 1080p, window 1440p: stretched
    [InlineData(1920, 40, 1920, 1080, 1920, 1080, true, 1920, 175, 1920, 810)]   // a window on the second monitor
    public void The_game_area_comes_from_the_typed_해상도(int cx, int cy, int cw, int ch, int w, int h, bool wide, int x, int y, int vw, int vh)
    {
        Assert.Equal(new Viewport(x, y, vw, vh), Geometry.ManualViewport(new PixelRect(cx, cy, cw, ch), new ManualScreen(w, h, wide)));
    }

    /// <summary>What a fit found is remembered for 직접 입력: the window's size, and 21:9 only for a 64:27 game area inside
    /// a taller window (a cutscene's bars, a window as wide as 21:9 itself, aren't the setting).</summary>
    [Theory]
    [InlineData(0, 0, 1920, 1080, 1920, 1080, false)]
    [InlineData(0, 135, 1920, 810, 1920, 1080, true)]
    [InlineData(0, 270, 3840, 1620, 3840, 2160, true)]
    [InlineData(0, 100, 1920, 880, 1920, 1080, false)]   // a cutscene's bars
    [InlineData(0, 0, 2560, 1080, 2560, 1080, false)]    // a 21:9 monitor: the game fills it
    public void A_fit_remembers_its_해상도_and_21_9(int x, int y, int vw, int vh, int cw, int ch, bool wide)
    {
        Assert.Equal(new ManualScreen(cw, ch, wide), Geometry.Detected(new Viewport(x, y, vw, vh), new PixelRect(0, 0, cw, ch)));
    }

    static (Image Band, PixelRect Rect) Band(string name, int dx = 0, int dy = 0)
    {
        var f = Fixture.Load(name);
        // Saying the capture lies dx, dy further on puts the bar there on screen: that far from where the typed values put it.
        return (f.Frames[0], new PixelRect(f.Rect[0] + dx, f.Rect[1] + dy, f.Rect[2], f.Rect[3]));
    }

    static readonly Viewport Fhd = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("band_110", 110)]
    [InlineData("band_Nocd", 110)]
    [InlineData("band_bard_charged", 100)]
    public void Right_values_place_the_bar_where_they_say(string name, double hud)
    {
        var (band, rect) = Band(name);
        var fit = FitSolver.SolveManual(band, rect, Fhd, hud);
        Assert.Equal(hud, fit.Fit!.Hud);
        Assert.Equal(0, fit.Moved);
        Assert.InRange(Math.Abs(fit.Fit.NudgeX) + Math.Abs(fit.Fit.NudgeY), 0, 3);
    }

    [Fact]
    public void Right_values_in_a_forced_21_9_area_place_the_bar()
    {
        var (band, rect) = Band("band_219");
        var fit = FitSolver.SolveManual(band, rect, new Viewport(0, 135, 1920, 810), 110);
        Assert.NotNull(fit.Fit);
        Assert.Equal(0, fit.Moved);
    }

    /// <summary>The bar off by more than the usual nudge (a position or ratio error): found by the wider search at the typed size,
    /// and the fit moved onto it.</summary>
    [Theory]
    [InlineData(25, -15)]
    [InlineData(-30, 20)]
    public void A_bar_off_its_spot_is_found_by_a_wider_search(int dx, int dy)
    {
        var (band, rect) = Band("band_110", dx, dy);
        var fit = FitSolver.SolveManual(band, rect, Fhd, 110);
        Assert.NotNull(fit.Fit);
        Assert.InRange(fit.Moved, Math.Max(Math.Abs(dx), Math.Abs(dy)) - 2, Math.Max(Math.Abs(dx), Math.Abs(dy)) + 2);
        // The fit's frame lands on the real one: its nudge (reference px) is the shift (screen px, scale 1 at HUD 110 1080p).
        Assert.InRange(fit.Fit!.NudgeX, dx - 2, dx + 2);
        Assert.InRange(fit.Fit.NudgeY, dy - 2, dy + 2);
    }

    /// <summary>Further off than the wider search: nothing is placed (the HP bar's row alone, right as it is, doesn't place it).</summary>
    [Fact]
    public void A_bar_further_off_than_the_search_places_nothing()
    {
        var (band, rect) = Band("band_110", -60, 0);
        Assert.Null(FitSolver.SolveManual(band, rect, Fhd, 110).Fit);
    }

    [Theory]
    [InlineData("band_110", 90)]    // a wrong HUD size: the frame is another size, the HP bar at another row
    [InlineData("band_80", 110)]
    public void Wrong_values_place_nothing(string name, double hud) =>
        Assert.Null(FitSolver.SolveManual(Band(name).Band, Band(name).Rect, Fhd, hud).Fit);

    [Fact]
    public void No_skill_bar_places_nothing()
    {
        var (band, rect) = Band("band_110");
        Assert.Null(FitSolver.SolveManual(new Image(band.Width, band.Height), rect, Fhd, 110).Fit);
    }
}
