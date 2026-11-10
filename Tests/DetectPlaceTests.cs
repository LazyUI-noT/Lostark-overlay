using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Placing a box on the 감지 page: which boxes 위치 조정 can move, which table rows each shows, and the one-shot read of a
/// picture that gives the box its green.</summary>
public class DetectPlaceTests
{
    static readonly Viewport Fhd = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("skill", AdjustPart.스킬창)]
    [InlineData("buff", AdjustPart.버프)]
    [InlineData("amsu", AdjustPart.보스)]
    [InlineData(DetectPlace.DebuffRow, AdjustPart.디버프)]
    [InlineData("g-jeonbun", AdjustPart.전분)]
    public void A_box_that_the_adjust_layer_moves_has_its_part(string id, AdjustPart part) => Assert.Equal(part, DetectPlace.PartOf(id));

    [Fact]
    public void The_panel_reads_green_on_a_real_panel_and_plain_on_nothing()
    {
        var panel = Fixture.Load("jeonbun_1").Frames[0];
        var at = new PixelRect(100, 100, panel.Width, panel.Height);

        Assert.True(PictureReads.Panel(panel));
        Assert.False(PictureReads.Panel(new Image(panel.Width, panel.Height)));
        Assert.True(PictureReads.Of(AdjustPart.전분, panel, at, Fhd, new Fit(100), 100, Geometry.BuffBoxCount, 100, at));
        Assert.Null(PictureReads.Of(AdjustPart.전분, panel, at, Fhd, new Fit(100), 100, Geometry.BuffBoxCount, 100, at with { X = 900 }));
    }

    [Fact]
    public void Every_movable_box_has_a_part_and_the_parts_are_all_covered() =>
        Assert.Equal(Enum.GetValues<AdjustPart>(), DetectPlace.Movable.Select(id => DetectPlace.PartOf(id)!.Value));

    [Theory]
    [InlineData("aden")]
    [InlineData("judg")]
    [InlineData("boss")]
    [InlineData("dungeon")]
    [InlineData("char")]
    [InlineData("meter")]
    public void A_box_it_cannot_move_has_no_part(string id) => Assert.Null(DetectPlace.PartOf(id));

    [Theory]
    [InlineData("skill", DetectPlace.Slots)]
    [InlineData("buff", DetectPlace.Buffs)]
    [InlineData(DetectPlace.DebuffRow, DetectPlace.Debuffs)]
    [InlineData("amsu", DetectPlace.Boss)]
    public void A_box_shows_its_own_table_rows(string id, string group) => Assert.Equal(group, DetectPlace.TableOf(id));

    [Fact]
    public void A_box_without_readings_in_the_table_shows_none() => Assert.Null(DetectPlace.TableOf("judg"));

    [Fact]
    public void Reads_of_a_part_are_the_adjust_layers()
    {
        var reads = new AdjustReads(true, false, null, true);
        Assert.True(reads.Of(AdjustPart.스킬창));
        Assert.False(reads.Of(AdjustPart.버프));
        Assert.Null(reads.Of(AdjustPart.디버프));
        Assert.True(reads.Of(AdjustPart.보스));
    }

    [Fact]
    public void The_bar_reads_well_on_a_picture_of_it_and_not_when_moved()
    {
        var f = Fixture.Load("band_110");
        var rect = new PixelRect(f.Rect[0], f.Rect[1], f.Rect[2], f.Rect[3]);
        var fit = FitSolver.Refine(f.Frames[0], rect, Fhd, FitSolver.Solve(f.Frames[0], rect, Fhd, video: true)!)!;
        double k = Geometry.Scale(Fhd, fit);

        Assert.True(PictureReads.Of(AdjustPart.스킬창, f.Frames[0], rect, Fhd, fit, 100, Geometry.BuffBoxCount, 100));
        Assert.False(PictureReads.Of(AdjustPart.스킬창, f.Frames[0], rect, Fhd, fit with { NudgeY = fit.NudgeY + 6 / k }, 100, Geometry.BuffBoxCount, 100));
    }

    [Fact]
    public void The_bar_is_not_judged_on_a_picture_that_leaves_out_its_band()
    {
        var small = new Image(100, 100);
        Assert.Null(PictureReads.Of(AdjustPart.스킬창, small, new PixelRect(900, 900, 100, 100), Fhd, new Fit(110), 100, Geometry.BuffBoxCount, 100));
    }

    [Fact]
    public void Buffs_read_on_a_picture_of_the_row()
    {
        var (name, vp, fit, size, rect) = BuffReaderTests.Bg;
        // buff_gameplay.mp4's row: some moments have buffs up, some not
        var reads = Fixture.Load(name).Frames.Select(f => PictureReads.Of(AdjustPart.버프, f, rect, vp, fit, size, 5, 100)).ToList();
        Assert.Contains(true, reads);
        Assert.DoesNotContain(null, reads);
    }

    [Fact]
    public void A_row_with_nothing_up_reads_as_nothing_seen_not_as_unknown()
    {
        var area = Geometry.BuffArea(Fhd, new Fit(110), 120);
        var dark = new Image(area.Width + 40, area.Height + 40);
        var at = new PixelRect(area.X - 20, area.Y - 20, dark.Width, dark.Height);
        Assert.False(PictureReads.Of(AdjustPart.버프, dark, at, Fhd, new Fit(110), 120, Geometry.BuffBoxCount, 100));
    }

    [Theory]
    [InlineData(AdjustPart.버프)]
    [InlineData(AdjustPart.디버프)]
    [InlineData(AdjustPart.보스)]
    public void A_row_is_not_judged_on_a_picture_that_leaves_it_out(AdjustPart part) =>
        Assert.Null(PictureReads.Of(part, new Image(50, 50), new PixelRect(0, 0, 50, 50), Fhd, new Fit(110), 120, Geometry.BuffBoxCount, 100));
}
