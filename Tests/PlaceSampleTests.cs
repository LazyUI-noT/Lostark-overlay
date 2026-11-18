using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>예시 보기 (rail-split.plan.md item 5): the sample picture lands under the box as the game drew it around that box.</summary>
public class PlaceSampleTests
{
    static readonly Viewport Hd = new(0, 0, 1920, 1080);
    static readonly PixelRect SourcePanel = new(1447, 877, 470, 114);

    const int SourceBossSize = 110;   // the 암수 sample's boss size (빛암 120_110_100.mp4)

    static FitAdjust At(Viewport vp, double hud, int buffCount = Geometry.BuffBoxCount, PixelRect? panel = null, int bossSize = SourceBossSize, int buffSize = 120) =>
        FitAdjust.Begin(vp, buffSize, new Fit(hud), buffCount, bossSize, panel ?? SourcePanel);

    [Theory]
    [InlineData(AdjustPart.버프)]
    [InlineData(AdjustPart.디버프)]
    public void The_buff_and_debuff_samples_follow_buff_size_not_the_hud(AdjustPart part)
    {
        var crop = PlaceSample.All[part].Crop;
        var small = PlaceSample.All[part].At(At(Hd, 110, buffSize: 100), part);
        Assert.Equal((int)Math.Round(crop.Width * 100 / 120.0), small.Width);
        Assert.Equal(crop.Width, PlaceSample.All[part].At(At(Hd, 80), part).Width);   // HUD 80, 버프 크기 120: icons as cut
    }

    [Fact]
    public void The_boss_sample_scales_with_the_boss_icons_not_the_hud()
    {
        var sample = PlaceSample.All[AdjustPart.보스];
        var big = sample.At(At(Hd, 110, bossSize: 120), AdjustPart.보스);
        double k = Geometry.BossScale(Hd, new Fit(110), 120) / Geometry.BossScale(Hd, new Fit(110), SourceBossSize);
        Assert.Equal((int)Math.Round(sample.Crop.Width * k), big.Width);
        Assert.True(big.Width > sample.Crop.Width);
    }

    public static TheoryData<AdjustPart> Parts => new() { AdjustPart.스킬창, AdjustPart.버프, AdjustPart.디버프, AdjustPart.보스, AdjustPart.전분 };

    [Theory, MemberData(nameof(Parts))]
    public void At_the_samples_own_screen_it_lands_where_it_was_cut(AdjustPart part) =>
        Assert.Equal(PlaceSample.All[part].Crop, PlaceSample.All[part].At(At(Hd, 110), part));

    [Theory, MemberData(nameof(Parts))]
    public void At_4k_it_is_twice_the_size_and_keeps_its_place_by_the_box(AdjustPart part)
    {
        var uhd = new Viewport(0, 0, 3840, 2160);
        var panel = SourcePanel with { X = SourcePanel.X * 2, Y = SourcePanel.Y * 2, Width = SourcePanel.Width * 2, Height = SourcePanel.Height * 2 };
        var crop = PlaceSample.All[part].Crop;
        var at = PlaceSample.All[part].At(At(uhd, 110, panel: panel), part);
        Assert.Equal((crop.Width * 2, crop.Height * 2), (at.Width, at.Height));
        Assert.InRange(at.X - crop.X * 2, -2, 2);   // the game's own layout doubles too: within rounding
        Assert.InRange(at.Y - crop.Y * 2, -2, 2);
    }

    [Fact]
    public void The_buff_sample_keeps_to_the_rows_right_end_whatever_the_count()
    {
        var few = PlaceSample.All[AdjustPart.버프].At(At(Hd, 110, buffCount: 6), AdjustPart.버프);
        Assert.Equal(PlaceSample.All[AdjustPart.버프].Crop, few);
    }

    [Fact]
    public void Every_sample_file_ships_with_the_app()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "App", "Assets", "examples");
        foreach (var s in PlaceSample.All.Values) Assert.True(File.Exists(Path.Combine(dir, s.File)), s.File);
    }
}
