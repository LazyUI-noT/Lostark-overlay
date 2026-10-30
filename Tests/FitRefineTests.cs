using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>테스트 모드's fine-tune (<see cref="FitSolver.Refine"/>) and the score behind it and the adjust layer's green.</summary>
public class FitRefineTests
{
    private static readonly Viewport Fhd = new(0, 0, 1920, 1080);

    private static (Image Band, PixelRect Rect) Load(string name)
    {
        var f = Fixture.Load(name);
        return (f.Frames[0], new PixelRect(f.Rect[0], f.Rect[1], f.Rect[2], f.Rect[3]));
    }

    /// <summary>The bar's nudge in screen pixels.</summary>
    private static (double X, double Y) ScreenNudge(Viewport vp, Fit fit)
    {
        double k = Geometry.Scale(vp, fit);
        return (fit.NudgeX * k, fit.NudgeY * k);
    }

    /// <summary>The picture moved by (dx, dy), its edge pixels repeated.</summary>
    private static Image Shift(Image src, int dx, int dy)
    {
        var dst = new Image(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int sx = Math.Clamp(x - dx, 0, src.Width - 1), sy = Math.Clamp(y - dy, 0, src.Height - 1);
                Array.Copy(src.Bgra, sy * src.Stride + sx * 4, dst.Bgra, y * dst.Stride + x * 4, 4);
            }
        return dst;
    }

    /// <summary>A 3×3 box blur: a stream's softness.</summary>
    private static Image Blur3(Image src)
    {
        var dst = new Image(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                            sum += src.Bgra[Math.Clamp(y + j, 0, src.Height - 1) * src.Stride + Math.Clamp(x + i, 0, src.Width - 1) * 4 + c];
                    dst.Bgra[y * dst.Stride + x * 4 + c] = (byte)(sum / 9);
                }
        return dst;
    }

    /// <summary>band_110 (HUD 110) shrunk by 0.75 about the screen's bottom centre: the bar a 1080p game draws at HUD 82.5, a size
    /// between the game's steps (a stream shown smaller). The crop (400, 900) lands at (540, 945).</summary>
    private static (Image Band, PixelRect Rect) Hud82_5()
    {
        var (src, rect) = Load("band_110");
        var small = ImageOps.Resample(src, new PixelRect(0, 0, rect.Width, rect.Height), 0.75);
        return (small, new PixelRect(540, 945, small.Width, small.Height));
    }

    /// <summary>Moved sideways only: blur leaves the HP bar's top edge as the one cue up and down, so a vertical move reads as size.</summary>
    [Fact]
    public void Refines_a_blurred_bar_between_HUD_steps()
    {
        var (small, rect) = Hud82_5();
        var band = Blur3(Shift(small, 3, 0));

        var fit = FitSolver.Refine(band, rect, Fhd, new Fit(FitSolver.HudFromHpBar(band, rect, Fhd)!.Value));

        Assert.NotNull(fit);
        Assert.InRange(fit.Hud, 82.0, 83.0);
        var (x, y) = ScreenNudge(Fhd, fit);
        Assert.InRange(x, 2, 4);
        Assert.InRange(y, -1, 1);
    }

    /// <summary>discord_stream.png (HUD ~90, forced 21:9): the bar sat within 3 px of the HP bar's spot.</summary>
    [Fact]
    public void Refines_the_discord_stream()
    {
        var (band, rect) = Load("band_discord");
        var vp = new Viewport(0, 135, 1920, 810);
        var start = FitSolver.Solve(band, rect, vp, video: true)!;

        var fit = FitSolver.Refine(band, rect, vp, start);

        Assert.NotNull(fit);
        Assert.InRange(fit.Hud, 89, 92);
        PixelRect got = Geometry.Slot(SlotId.각성기, vp, fit), was = Geometry.Slot(SlotId.각성기, vp, start);
        Assert.InRange(got.X - was.X, -3, 3);
        Assert.InRange(got.Y - was.Y, -3, 3);
    }

    [Theory]
    [InlineData("band_110", 0, 0, 1920, 1080)]
    [InlineData("band_1440", 1080, 162, 2560, 1440)]
    [InlineData("band_4k", 0, 0, 3840, 2160)]
    [InlineData("band_4k219", 0, 270, 3840, 1620)]
    [InlineData("band_219", 0, 135, 1920, 810)]
    public void Refine_keeps_a_good_fit(string name, int x, int y, int w, int h)
    {
        var (band, rect) = Load(name);
        var vp = new Viewport(x, y, w, h);
        var start = FitSolver.Solve(band, rect, vp)!;

        var fit = FitSolver.Refine(band, rect, vp, start);

        Assert.NotNull(fit);
        Assert.InRange(fit.Hud - start.Hud, -0.5, 0.5);
        foreach (var slot in SlotInfo.All)
        {
            PixelRect got = Geometry.Slot(slot, vp, fit), was = Geometry.Slot(slot, vp, start);
            Assert.True(Math.Abs(got.X - was.X) <= 1 && Math.Abs(got.Y - was.Y) <= 1, $"{slot}: {got} vs {was}");
        }
    }

    [Fact]
    public void Refine_finds_nothing_on_a_dark_band()
    {
        var (band, rect) = Load("band_110");
        var dark = new Image(band.Width, band.Height);
        Assert.Null(FitSolver.Refine(dark, rect, Fhd, new Fit(100)));
    }

    [Fact]
    public void Score_is_higher_on_the_bar_than_3_px_off()
    {
        var (band, rect) = Load("band_110");
        double k = Geometry.Scale(Fhd, new Fit(110)), on = FitSolver.Score(band, rect, Fhd, new Fit(110));
        Assert.True(on > FitSolver.Score(band, rect, Fhd, new Fit(110, 3 / k, 0)));
        Assert.True(on > FitSolver.Score(band, rect, Fhd, new Fit(110, 0, 3 / k)));
    }

    public static TheoryData<string, int, int, int, int> AllBands => new()
    {
        { "band_80", 0, 0, 1920, 1080 }, { "band_90", 0, 0, 1920, 1080 }, { "band_105", 0, 0, 1920, 1080 }, { "band_110", 0, 0, 1920, 1080 },
        { "band_Nocd", 0, 0, 1920, 1080 }, { "band_gameplay", 0, 0, 1920, 1080 }, { "band_333", 0, 0, 1920, 1080 },
        { "band_bard_charged", 0, 0, 1920, 1080 }, { "band_greyed", 0, 0, 1920, 1080 }, { "band_buff", 0, 0, 1920, 1080 },
        { "band_b80", 0, 0, 1920, 1080 }, { "band_discord", 0, 135, 1920, 810 }, { "band_1440", 1080, 162, 2560, 1440 },
        { "band_4k", 0, 0, 3840, 2160 }, { "band_4k219", 0, 270, 3840, 1620 }, { "band_219", 0, 135, 1920, 810 },
    };

    /// <summary>ReadsWell's threshold, pinned on every band: the refined fit reads well; 3 px up or down, or 6 px off, does not.</summary>
    [Theory, MemberData(nameof(AllBands))]
    public void The_refined_fit_reads_well_and_a_moved_one_does_not(string name, int x, int y, int w, int h)
    {
        var (band, rect) = Load(name);
        var vp = new Viewport(x, y, w, h);
        var fit = FitSolver.Refine(band, rect, vp, FitSolver.Solve(band, rect, vp, video: true)!)!;
        double k = Geometry.Scale(vp, fit);
        Fit Moved(double dx, double dy) => fit with { NudgeX = fit.NudgeX + dx / k, NudgeY = fit.NudgeY + dy / k };

        Assert.True(FitSolver.ReadsWell(FitSolver.Score(band, rect, vp, fit)));
        foreach (var off in new[] { Moved(0, 3), Moved(0, -3), Moved(6, 6), Moved(-6, -6) })
            Assert.False(FitSolver.ReadsWell(FitSolver.Score(band, rect, vp, off)), $"{off}");
    }

    /// <summary>A red edge with no skill bar under it (a red stripe across the band): Solve's video path keeps the size, Refine
    /// places nothing.</summary>
    [Fact]
    public void Refine_finds_nothing_on_a_red_edge_alone()
    {
        var (src, rect) = Load("band_110");
        var band = new Image(src.Width, src.Height);
        for (int y = 947 - rect.Y; y < band.Height; y++)
            for (int x = 0; x < band.Width; x++)
                (band.Bgra[y * band.Stride + x * 4], band.Bgra[y * band.Stride + x * 4 + 1], band.Bgra[y * band.Stride + x * 4 + 2]) = (20, 20, 200);

        Assert.NotNull(FitSolver.Solve(band, rect, Fhd, video: true));
        Assert.Null(FitSolver.Refine(band, rect, Fhd, new Fit(110)));
    }
}
