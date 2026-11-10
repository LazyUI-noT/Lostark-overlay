using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The 그림자 레이드 gimmick (세르카, 벨가르딘): the boss bar goes and a fill bar with the damage dealt in it shows
/// ("134,046,661,186"). Fixtures shadow_number (every 2 s from 9:03 of "Lost Ark 2026.11.03 - 21.46.27.02.mp4", 1080p HUD 110;
/// frames 2–4 caught the number popping bigger as it jumps) and shadow_bossbar (the normal bar just before, same area).</summary>
public class ShadowGimmickTests
{
    [Fact]
    public void The_count_window_follows_the_bar_else_the_gimmick_lines()
    {
        var gimmick = new ShadowHp(Start: 1000, Max: 5000, Dealt: 250, PerLine: 100);   // 750 left: 8 lines
        Assert.Equal(12, ShadowRead.BarLines(12, gimmick));
        Assert.Equal(8, ShadowRead.BarLines(null, gimmick));
        Assert.Null(ShadowRead.BarLines(null, gimmick with { Over = 4 }));   // dead: nothing left to count
        Assert.Null(ShadowRead.BarLines(null, null));
    }

    static readonly long?[] Shown =
    [
        null, 161_252_862, 21_643_927_603, 31_575_410_844, 40_812_997_885, 72_000_567_057, 84_460_927_882, 95_398_267_131,
        98_119_684_193, 101_122_012_611, 103_938_369_895, 134_046_661_186, 166_122_959_334, 199_744_020_906, 210_082_561_219,
        238_620_583_824, 239_674_092_807, 248_039_915_861, 249_292_970_381, 249_755_521_722,
    ];

    [Fact]
    public void The_hp_text_sits_in_the_gimmicks_fill_bar_under_its_number()
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110));
        var bar = Geometry.ShadowBar(vp, fit);
        Assert.Equal(new PixelRect(638, 101, 645, 21), bar);   // the fill measured at 9:05 and 9:30 of the video
        Assert.True(bar.Y >= Geometry.ShadowNumber(vp, fit).Bottom);
        var big = Geometry.ShadowBar(new Viewport(0, 0, 2560, 1440), fit);   // follows the resolution from the top centre
        Assert.InRange(big.X + big.Width / 2, 1278, 1282);
        Assert.InRange(big.Height, 27, 29);
    }

    [Fact]
    public void The_gimmicks_number_reads_in_every_frame_also_while_it_pops()
    {
        var frames = Fixture.Load("shadow_number").Frames;
        Assert.Equal(Shown.Length, frames.Count);
        for (int i = 0; i < frames.Count; i++)
            Assert.True(BossDigits.ReadNumber(frames[i], 1) == Shown[i], $"frame {i}: {BossDigits.ReadNumber(frames[i], 1)}");
    }

    /// <summary>The number's white turned to a colour, as the game tints it at the gimmick's end (yellow at 9:54 of the same video):
    /// the text only, each pixel by how white it was; the frame around it stays.</summary>
    static Image Tinted(Image img, byte r, byte g, byte b)
    {
        var px = (byte[])img.Bgra.Clone();
        for (int i = 0; i < px.Length; i += 4)
        {
            int white = Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
            double w = Math.Clamp((white - 60) / 140.0, 0, 1);
            byte Mix(byte was, byte to) => (byte)(was * (1 - w) + to * white / 255.0 * w);
            (px[i], px[i + 1], px[i + 2]) = (Mix(px[i], b), Mix(px[i + 1], g), Mix(px[i + 2], r));
        }
        return new Image(img.Width, img.Height, px);
    }

    [Theory]
    [InlineData(255, 200, 30)]   // yellow, as at 9:54
    [InlineData(255, 150, 40)]   // orange
    [InlineData(255, 70, 70)]    // red
    public void The_gimmicks_number_reads_in_colour_too(byte r, byte g, byte b)
    {
        var frames = Fixture.Load("shadow_number").Frames;
        for (int i = 0; i < frames.Count; i++)
            Assert.True(BossDigits.ReadNumber(Tinted(frames[i], r, g, b), 1) == Shown[i], $"frame {i}: {BossDigits.ReadNumber(Tinted(frames[i], r, g, b), 1)}");
    }

    /// <summary>shadow_number_sizes (band x 760–1160, y 36–96 at 1080p HUD 110): the number's glyphs 16 px tall (0, 5, 7),
    /// settling 17–18 (2, 3, 8) and popped 20–21 (1, 4, 6, 9; tops at y 56), as ShadowNumberSweep read them.</summary>
    static readonly long[] Sized =
    [
        161_252_862, 21_643_927_603, 22_182_231_521, 95_161_110_567, 101_070_264_615, 107_682_820_671, 200_317_003_987,
        249_292_970_381, 311_947_291_908, 400_033_765_300,
    ];
    static readonly int[] Normal = [0, 5, 7];

    /// <summary>The pop grows the number about this point (1080p HUD 110): 16 px at y 64–80 and 21 px at y 56–77 both scale from
    /// it, so a 1.5× pop (the most ReadNumber allows) reaches y 51–75.</summary>
    const double PopX = 960, PopY = 91;

    /// <summary>The band drawn as the game would on <paramref name="vp"/> at <paramref name="hud"/>, the number popped by
    /// <paramref name="pop"/>: the top rows of the screen, mapped from the top centre like Geometry.ShadowNumber (bilinear;
    /// black outside the band). <paramref name="at"/>: the band's top-left at 1080p HUD 110 (its fixture's rect).</summary>
    static Image Drawn(Image band, (int X, int Y) at, Viewport vp, double hud, double pop)
    {
        double k = Geometry.Scale(vp, new Fit(hud));
        int w = vp.Width, h = (int)Math.Ceiling(100 * k);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double rx = PopX + ((x + 0.5 - w / 2.0) / k + 960 - PopX) / pop - at.X - 0.5;
                double ry = PopY + ((y + 0.5) / k - PopY) / pop - at.Y - 0.5;
                int x0 = (int)Math.Floor(rx), y0 = (int)Math.Floor(ry);
                if (x0 < 0 || y0 < 0 || x0 + 1 >= band.Width || y0 + 1 >= band.Height) continue;
                double fx = rx - x0, fy = ry - y0;
                for (int c = 0; c < 3; c++)
                {
                    double At(int xx, int yy) => band.Bgra[(yy * band.Width + xx) * 4 + c];
                    double v = (At(x0, y0) * (1 - fx) + At(x0 + 1, y0) * fx) * (1 - fy) + (At(x0, y0 + 1) * (1 - fx) + At(x0 + 1, y0 + 1) * fx) * fy;
                    px[(y * w + x) * 4 + c] = (byte)Math.Round(v);
                }
                px[(y * w + x) * 4 + 3] = 255;
            }
        return new Image(w, h, px);
    }

    static long? ReadOn(Image band, (int X, int Y) at, Viewport vp, double hud, double pop)
    {
        var fit = new Fit(hud);
        return BossDigits.ReadNumber(Drawn(band, at, vp, hud, pop).Crop(Geometry.ShadowNumber(vp, fit)), Geometry.Scale(vp, fit));
    }

    static (IReadOnlyList<Image> Frames, (int X, int Y) At) Band(string name) =>
        Fixture.Load(name) is var f ? (f.Frames, (f.Rect[0], f.Rect[1])) : default;

    [Theory]
    [InlineData(1920, 1080, 110)]
    [InlineData(1920, 1080, 100)]
    [InlineData(2560, 1440, 110)]
    [InlineData(3840, 2160, 110)]
    [InlineData(2560, 1080, 110)]   // 21:9
    [InlineData(3440, 1440, 100)]   // 21:9
    public void The_gimmicks_number_reads_at_every_size_it_takes(int width, int height, double hud)
    {
        var (frames, at) = Band("shadow_number_sizes");
        var vp = new Viewport(0, 0, width, height);
        var misses = new List<string>();
        for (int i = 0; i < frames.Count; i++)
            if (ReadOn(frames[i], at, vp, hud, 1) is var read && read != Sized[i]) misses.Add($"frame {i}: {read}");
        foreach (int i in Normal)   // popped to the most the reader allows
            if (ReadOn(frames[i], at, vp, hud, 1.5) is var read && read != Sized[i]) misses.Add($"frame {i} at 1.5×: {read}");
        Assert.Empty(misses);
    }

    /// <summary>The normal boss bar (shadow_bossbar_band: its red name and "current/max" text, shown only outside 대난투, which
    /// shows no boss HP at all) through the same
    /// layouts: the number's area, taller for the pop, must not read the name or the HP text as a number.</summary>
    [Theory]
    [InlineData(1920, 1080, 110)]
    [InlineData(1920, 1080, 100)]
    [InlineData(2560, 1440, 110)]
    [InlineData(3840, 2160, 110)]
    [InlineData(2560, 1080, 110)]   // 21:9
    [InlineData(3440, 1440, 100)]   // 21:9
    public void The_normal_boss_bar_reads_no_number_at_any_layout(int width, int height, double hud)
    {
        var vp = new Viewport(0, 0, width, height);
        foreach (var name in new[] { "shadow_bossbar", "shadow_bossbar_band" })
        {
            var (frames, at) = Band(name);
            Assert.All(frames, f => Assert.Null(ReadOn(f, at, vp, hud, 1)));
        }
    }

    [Fact]
    public void The_normal_boss_bars_hp_text_is_no_number() =>
        Assert.All(Fixture.Load("shadow_bossbar").Frames, f => Assert.Null(BossDigits.ReadNumber(f, 1)));
}
