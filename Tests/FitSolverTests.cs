using System.Text;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class FitSolverTests
{
    [Theory]
    [InlineData("band_80", 80)]
    [InlineData("band_90", 90)]
    [InlineData("band_105", 105)]
    [InlineData("band_110", 110)]
    [InlineData("band_Nocd", 110)]
    [InlineData("band_gameplay", 100)]
    [InlineData("band_333", 110)]
    public void Finds_the_HUD_size_from_the_awakening_frame(string band, double hud) => AssertFit(band, Fhd, hud);

    /// <summary>bardbug_103.mp4 (바드, HUD 100): the 각성기 frame is the charged one, a thick orange border that matches the
    /// plain gold template only 0.55, while the T slot's grey frame beside it looks like a HUD 80 각성기 frame (0.75).
    /// The HP bar sets the size, so the frame only places it.</summary>
    [Fact]
    public void A_charged_awakening_frame_is_not_mistaken_for_the_slot_beside_it()
    {
        var band = Fixture.Load("band_bard_charged");
        var rect = new PixelRect(band.Rect[0], band.Rect[1], band.Rect[2], band.Rect[3]);
        foreach (var frame in band.Frames)
            Assert.InRange(FitSolver.Solve(frame, rect, Fhd)!.Hud, 98, 102);
    }

    /// <summary>discord_stream.png (바드, HUD ~90, forced 21:9): the stream's blur takes the 각성기 ring below the game's check,
    /// though the bar sits where the HP bar puts it. 테스트 모드 (video) takes the HP bar's size alone.</summary>
    [Fact]
    public void A_blurred_stream_is_fitted_from_the_hp_bar_in_test_mode()
    {
        var band = Fixture.Load("band_discord");
        var rect = new PixelRect(band.Rect[0], band.Rect[1], band.Rect[2], band.Rect[3]);
        var vp = new Viewport(0, 135, 1920, 810);
        Assert.Null(FitSolver.Solve(band.Frames[0], rect, vp));
        Assert.InRange(FitSolver.Solve(band.Frames[0], rect, vp, video: true)!.Hud, 89, 92);
    }

    /// <summary>The HP bar's top edge sits 1.21 × HUD px above the bottom of a 1080-high game area for every class
    /// (80percent–110percent.png: 983, 971, 953, 947).</summary>
    [Theory]
    [InlineData("band_80", 80)]
    [InlineData("band_90", 90)]
    [InlineData("band_105", 105)]
    [InlineData("band_110", 110)]
    [InlineData("band_gameplay", 100)]
    [InlineData("band_bard_charged", 100)]
    public void Reads_the_HUD_size_from_the_HP_bar(string name, double hud)
    {
        var band = Fixture.Load(name);
        var rect = new PixelRect(band.Rect[0], band.Rect[1], band.Rect[2], band.Rect[3]);
        Assert.InRange(FitSolver.HudFromHpBar(band.Frames[0], rect, Fhd)!.Value, hud - 1, hud + 1);
    }

    /// <summary>The same away from 1080p: 1440p_hud100_120buff.png (the game at 1080,162 on the second monitor), the 4K
    /// stills and the forced 21:9 shots.</summary>
    [Theory]
    [InlineData("band_1440", 1080, 162, 2560, 1440, 100)]
    [InlineData("band_4k", 0, 0, 3840, 2160, 100)]
    [InlineData("band_4k219", 0, 270, 3840, 1620, 100)]
    [InlineData("band_219", 0, 135, 1920, 810, 110)]
    public void Reads_the_HUD_size_from_the_HP_bar_at_any_resolution(string name, int x, int y, int w, int h, double hud)
    {
        var band = Fixture.Load(name);
        var rect = new PixelRect(band.Rect[0], band.Rect[1], band.Rect[2], band.Rect[3]);
        Assert.InRange(FitSolver.HudFromHpBar(band.Frames[0], rect, new Viewport(x, y, w, h))!.Value, hud - 1, hud + 1);
    }

    [Fact]
    public void Finds_the_fit_in_a_forced_21_9_viewport() => AssertFit("band_219", new Viewport(0, 135, 1920, 810), 110);

    [Fact]
    public void Refines_continuously_when_no_HUD_step_fits()
    {
        // The HUD 110 band shrunk by 0.85: a scale between the game's steps, as a 1440p/4K rule might give.
        var src = Fixture.Load("band_Nocd");
        var img = src.Frames[0];
        const double k = 0.85;
        int w = (int)(img.Width * k), h = (int)(img.Height * k);
        var small = new Image(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Box-average the source pixels this output pixel covers.
                int x0 = (int)(x / k), x1 = Math.Max(x0 + 1, (int)((x + 1) / k)), y0 = (int)(y / k), y1 = Math.Max(y0 + 1, (int)((y + 1) / k));
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++) sum += img.Bgra[yy * img.Stride + xx * 4 + c];
                    small.Bgra[y * small.Stride + x * 4 + c] = (byte)(sum / ((x1 - x0) * (y1 - y0)));
                }
            }
        // Viewport whose bottom centre maps the shrunk band the same way: scale 0.85 at HUD 110 means height 918.
        var vp = new Viewport((int)Math.Round(960 - 960 * k), (int)Math.Round(1080 - 1080 * k), (int)Math.Round(1920 * k), (int)Math.Round(1080 * k));
        var bandRect = new PixelRect(vp.X + (int)Math.Round(400 * k), vp.Y + (int)Math.Round(900 * k), w, h);
        var fit = FitSolver.Solve(small, bandRect, vp);
        Assert.NotNull(fit);
        Assert.InRange(fit.Hud, 108, 112);
    }

    [Theory]
    [InlineData(1080, 80)]
    [InlineData(1080, 110)]
    [InlineData(810, 80)]
    [InlineData(810, 110)]
    public void Band_contains_the_awakening_frame_at_any_HUD_size(int height, double hud)
    {
        var vp = new Viewport(0, 1080 - height, 1920, height);
        PixelRect band = FitSolver.Band(vp), frame = Geometry.ToScreen(Geometry.RefAwakeningFrame, vp, new Fit(hud));
        Assert.True(frame.X >= band.X && frame.Y >= band.Y && frame.Right <= band.Right && frame.Bottom <= band.Bottom, $"{frame} in {band}");
        Assert.Equal(vp.Y + vp.Height, band.Bottom);
    }

    [Fact]
    public void Returns_null_without_a_skill_bar()
    {
        var band = Fixture.Load("band_Nocd");
        var dark = new Image(band.Frames[0].Width, band.Frames[0].Height);
        Assert.Null(FitSolver.Solve(dark, new PixelRect(400, 900, dark.Width, dark.Height), Fhd));
    }

    private static void AssertFit(string name, Viewport vp, double hud)
    {
        var band = Fixture.Load(name);
        var rect = new PixelRect(band.Rect[0], band.Rect[1], band.Rect[2], band.Rect[3]);
        var fit = FitSolver.Solve(band.Frames[0], rect, vp);
        Assert.NotNull(fit);
        Assert.InRange(fit.Hud, hud - 2, hud + 2);
        foreach (var slot in SlotInfo.All)
        {
            PixelRect got = Geometry.Slot(slot, vp, fit), want = Geometry.Slot(slot, vp, new Fit(hud));
            Assert.True(Math.Abs(got.X - want.X) <= 2 && Math.Abs(got.Y - want.Y) <= 2
                && Math.Abs(got.Width - want.Width) <= 2 && Math.Abs(got.Height - want.Height) <= 2, $"{slot}: {got} vs {want}");
        }
    }

    /// <summary>Rewrites Core/Vision/FrameTemplate.cs from six HUD 110 shots. Runs only with
    /// GAUGE_BUILD_TEMPLATES=1, because it edits the source tree.</summary>
    [Fact]
    public void Build_frame_template_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TEMPLATES") != "1") return;
        PixelRect f = Geometry.RefAwakeningFrame, icon = Geometry.RefSlot(SlotId.각성기);
        // The frame at HUD 110 in six shots (fixture, frame's top-left in it). A light runs around the frame and
        // the panel is see-through, so the per-pixel median keeps only what every shot shares.
        var shots = new[] { ("band_Nocd", 105, 82), ("band_110", 105, 82), ("band_333", 105, 82), ("frame_cd", 0, 0), ("frame_nomana", 0, 0), ("frame_silenced", 0, 0) }
            .Select(s => (Img: Fixture.Load(s.Item1).Frames[0], X: s.Item2, Y: s.Item3)).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("namespace GaugeOverlay.Core.Vision;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The gold 각성기 frame (Geometry.RefAwakeningFrame) at reference scale: per-pixel median of six HUD 110 shots. Generated by");
        sb.AppendLine("/// FitSolverTests.Build_frame_template_when_requested (GAUGE_BUILD_TEMPLATES=1); do not edit by hand.");
        sb.AppendLine("/// One string per row; \" .:-=+*#%@\" is grey (R+G+B)/3 0..255 in 10 steps; '?' is ignored: the class-specific icon, and pixels that differ by more than 40 between shots (the light running around the frame, background seen through the panel).</summary>");
        sb.AppendLine("internal static class FrameTemplate");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly string[] Rows =");
        sb.AppendLine("    [");
        for (int y = f.Y; y < f.Bottom; y++)
        {
            var row = new char[f.Width];
            for (int x = f.X; x < f.Right; x++)
            {
                bool inIcon = x >= icon.X && x < icon.Right && y >= icon.Y && y < icon.Bottom;
                var grey = shots.Select(s => s.Img.Pixel(s.X + x - f.X, s.Y + y - f.Y)).Select(p => (p.B + p.G + p.R) / 3).Order().ToArray();
                int median = (grey[2] + grey[3]) / 2;
                bool stable = grey[^1] - grey[0] <= 40;
                row[x - f.X] = inIcon || !stable ? '?' : DigitReader.GreyRamp[Math.Min(9, median * 10 / 256)];
            }
            sb.AppendLine($"        \"{new string(row)}\",");
        }
        sb.AppendLine("    ];");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(RepoRoot(), "Core", "Vision", "FrameTemplate.cs"), sb.ToString().ReplaceLineEndings("\r\n"));
    }
}
