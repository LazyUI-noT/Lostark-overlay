using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>편집 모드의 칸 배치: where each box sits (DIP at size 1.0), snapping, and sizes shared within a set.</summary>
public class FreeLayoutTests
{
    [Fact]
    public void Default_is_the_fixed_grid_with_the_gauge_on_top()
    {
        var d = FreeLayout.Default(gaugeThickness: 6);

        Assert.Equal(new Cell(0, 0, 250, 6), d[Part.게이지]);
        Assert.Equal(new Cell(0, 8, 34, 14), d[Part.변화]);
        Assert.Equal(new Cell(0, 24, 34, 54), d[Part.각성기]);
        Assert.Equal(new Cell(36, 8, 34, 70), d[Part.초각스]);
        Assert.Equal(new Cell(72, 8, 34, 34), d[Part.일반1]);
        Assert.Equal(new Cell(72 + 3 * 36, 8 + 36, 34, 34), d[Part.일반8]);
        Assert.Equal(new Cell(216, 8, 34, 34), d[Part.아덴1]);
        Assert.Equal(new Cell(216, 44, 34, 34), d[Part.아덴2]);
    }

    [Fact]
    public void The_buff_bars_sit_under_the_grid_in_two_rows_and_resize_alone()
    {
        var d = FreeLayout.Default(gaugeThickness: 6);

        Assert.Equal(new Cell(0, 80, 124, 12), d[Part.버프공증]);
        Assert.Equal(new Cell(126, 80, 124, 12), d[Part.버프아덴]);
        Assert.Equal(new Cell(0, 94, 124, 12), d[Part.버프초각]);
        Assert.Equal(new Cell(126, 94, 124, 12), d[Part.버프뎀증]);
        Assert.Equal([Part.버프아덴], FreeLayout.Linked(Part.버프아덴).Parts);
    }

    [Fact]
    public void A_box_snaps_beside_another_one_gap_apart_and_lines_up()
    {
        var other = new Cell(100, 100, 34, 34);

        // 3 DIP right of "one gap past its right edge", 2 DIP below its top.
        var moved = FreeLayout.SnapMove(new Cell(100 + 34 + 2 + 3, 102, 34, 34), [other], FreeLayout.SnapDistance);

        Assert.Equal(new Cell(136, 100, 34, 34), moved);
    }

    [Fact]
    public void A_box_far_from_the_others_stays_where_it_was_dropped()
    {
        var cell = new Cell(300, 300, 34, 34);
        Assert.Equal(cell, FreeLayout.SnapMove(cell, [new Cell(0, 0, 34, 34)], FreeLayout.SnapDistance));
    }

    [Fact]
    public void Resizing_snaps_the_far_edges()
    {
        var other = new Cell(100, 0, 34, 70);

        var sized = FreeLayout.SnapResize(new Cell(0, 0, 95, 66), [other], FreeLayout.SnapDistance);

        Assert.Equal(new Cell(0, 0, 98, 70), sized);   // right edge one gap before it, bottom level with its bottom
    }

    [Fact]
    public void With_sizes_linked_the_eight_keys_resize_together()
    {
        var d = FreeLayout.Default(6);

        var r = FreeLayout.Resize(d, Part.일반3, 40, 30, linked: true);

        Assert.All(Enumerable.Range(0, 8).Select(i => r[Part.일반1 + i]), c => Assert.Equal((40.0, 30.0), (c.W, c.H)));
        // Still a 4 × 2 grid one gap apart: each box moves by the growth of the linked boxes before it in its row/column.
        Assert.Equal(new Cell(72, 8, 40, 30), r[Part.일반1]);
        Assert.Equal(new Cell(72 + 3 * 42, 8, 40, 30), r[Part.일반4]);
        Assert.Equal(new Cell(72, 8 + 32, 40, 30), r[Part.일반5]);
        Assert.Equal(new Cell(72 + 3 * 42, 8 + 32, 40, 30), r[Part.일반8]);
        Assert.Equal(d[Part.초각스], r[Part.초각스]);
    }

    [Fact]
    public void 각성기_and_its_변화_box_share_only_their_width()
    {
        var d = FreeLayout.Default(6);

        var r = FreeLayout.Resize(d, Part.각성기, 50, 60, linked: true);

        Assert.Equal((50.0, 60.0), (r[Part.각성기].W, r[Part.각성기].H));
        Assert.Equal((50.0, d[Part.변화].H), (r[Part.변화].W, r[Part.변화].H));
    }

    [Fact]
    public void Unlinked_only_the_box_resizes_and_the_input_is_left_alone()
    {
        var d = FreeLayout.Default(6);

        var r = FreeLayout.Resize(d, Part.일반3, 40, 30, linked: false);

        Assert.Equal((40.0, 30.0), (r[Part.일반3].W, r[Part.일반3].H));
        Assert.Equal(d[Part.일반4], r[Part.일반4]);
        Assert.Equal(34, d[Part.일반3].W);
    }

    [Fact]
    public void A_box_never_shrinks_below_the_minimum()
    {
        var r = FreeLayout.Resize(FreeLayout.Default(6), Part.초각스, 1, -5, linked: false);
        Assert.Equal((FreeLayout.MinSize, FreeLayout.MinSize), (r[Part.초각스].W, r[Part.초각스].H));
    }
}
