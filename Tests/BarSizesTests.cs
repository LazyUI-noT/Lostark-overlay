using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The buff bars' 길이 and 두께 in a free layout, and each debuff box's own size (BarSizes).</summary>
public class BarSizesTests
{
    /// <summary>A flat bar drains from its left end: 길이 shortens it from the right, 두께 thickens it downwards.</summary>
    [Fact]
    public void A_flat_bar_keeps_its_left_end()
    {
        Assert.Equal(new Cell(10, 20, 30, 24), BarSizes.Sized(new Cell(10, 20, 60, 12), length: 0.5, thickness: 2));
    }

    /// <summary>An upright bar drains from its bottom: 길이 shortens it from the top, 두께 widens it to the right.</summary>
    [Fact]
    public void An_upright_bar_keeps_its_bottom()
    {
        Assert.Equal(new Cell(0, 20, 12, 20), BarSizes.Sized(new Cell(0, 0, 6, 40), length: 0.5, thickness: 2));
    }

    /// <summary>Turning the fixed grid into a free layout keeps each bar's spot, not its drawn size, so 길이 and 두께
    /// aren't applied twice.</summary>
    [Theory]
    [InlineData(10, 20, 60, 12, false)]
    [InlineData(0, 0, 6, 40, true)]
    public void Unsized_undoes_sized(double x, double y, double w, double h, bool upright)
    {
        var cell = new Cell(x, y, w, h);
        var drawn = BarSizes.Sized(cell, 0.1, 3);
        var back = BarSizes.Unsized(drawn, 0.1, 3, upright);
        Assert.Equal(cell.X, back.X, 6);
        Assert.Equal(cell.Y, back.Y, 6);
        Assert.Equal(cell.W, back.W, 6);
        Assert.Equal(cell.H, back.H, 6);
    }

    /// <summary>A corner drag grows the cell so the drawn corner follows the pointer: what moves it is the drawn size per
    /// cell size, along each axis.</summary>
    [Theory]
    [InlineData(10, 20, 60, 12)]
    [InlineData(0, 0, 6, 40)]
    public void A_corner_drag_moves_the_drawn_corner_by_the_factors(double x, double y, double w, double h)
    {
        var cell = new Cell(x, y, w, h);
        var (fx, fy) = BarSizes.DragFactors(cell, 0.5, 2);
        Cell before = BarSizes.Sized(cell, 0.5, 2), after = BarSizes.Sized(cell with { W = w + 4, H = h + 4 }, 0.5, 2);
        Assert.Equal((4 * fx, 4 * fy), (after.Right - before.Right, after.Bottom - before.Bottom));
    }

    [Fact]
    public void Sizes_are_kept_to_what_the_sliders_offer()
    {
        var bar = new BuffIndicator { Length = 5, Thickness = 0 };
        Assert.Equal((1.0, 0.25), (BarSizes.Length(bar), BarSizes.Thickness(bar)));
    }

    /// <summary>The fixed grid's bar dragged by its far end (내 오버레이): 길이 grows by as much as the end moved, as a share of
    /// what is drawn, kept to what the slider offered.</summary>
    [Fact]
    public void An_end_drag_sets_a_bars_length()
    {
        Assert.Equal(0.75, BarSizes.LengthDragged(0.5, drawn: 40, full: 80, by: 20));
        Assert.Equal(BarSizes.MaxLength, BarSizes.LengthDragged(0.5, drawn: 40, full: 80, by: 900));
        Assert.Equal(BarSizes.MinLength, BarSizes.LengthDragged(0.5, drawn: 40, full: 80, by: -900));
        Assert.Equal(0.5, BarSizes.LengthDragged(0.5, drawn: 40, full: 0, by: 10));
    }

    /// <summary>The drag stops a bar at 5 px drawn (user, 2026-11-13); one already shorter doesn't shrink or jump.</summary>
    [Fact]
    public void An_end_drag_stops_a_bar_at_five_pixels()
    {
        Assert.Equal(BarSizes.MinLength, BarSizes.LengthDragged(0.5, drawn: 100, full: 200, by: -98));   // 5 px would be 0.025: 길이's own floor comes first
        Assert.Equal(0.5, BarSizes.LengthDragged(0.5, drawn: 4, full: 8, by: -2));
        Assert.Equal(0.2, BarSizes.LengthDragged(0.8, drawn: 20, full: 25, by: -19));    // 20 px → 5 px
    }

    /// <summary>A bar drawn at the renderer's 2 px floor (길이 0.1 of 13 px is 1.3 px): its end follows the pointer from the
    /// 2 px drawn, so the first move doesn't lag behind it (user, 2026-11-13).</summary>
    [Fact]
    public void An_end_drag_follows_the_pointer_from_a_floored_bar()
    {
        Assert.Equal(0.23, BarSizes.LengthDragged(0.1, drawn: 2, full: 13, by: 1));   // 3 px of 13
    }

    [Fact]
    public void Debuff_boxes_stand_in_a_row_at_the_window_size_by_default()
    {
        var boxes = BarSizes.DebuffBoxes(new DebuffSettings(), w: 30, h: 20, gap: 2);
        Assert.Equal([new Cell(0, 0, 30, 20), new Cell(32, 0, 30, 20), new Cell(64, 0, 30, 20)], boxes);
    }

    /// <summary>Each box has its own 너비 and 높이; the ones after a wider box move along, and the window grows to the tallest.</summary>
    [Fact]
    public void Each_debuff_box_has_its_own_size()
    {
        var d = new DebuffSettings();
        (d.Box(1).Width, d.Box(1).Height) = (2, 1.5);
        var boxes = BarSizes.DebuffBoxes(d, w: 30, h: 20, gap: 2);
        Assert.Equal([new Cell(0, 0, 30, 20), new Cell(32, 0, 60, 30), new Cell(94, 0, 30, 20)], boxes);
    }

    [Fact]
    public void A_hand_edited_debuff_box_size_is_kept_in_range()
    {
        var d = new DebuffSettings();
        (d.Box(2).Width, d.Box(2).Height) = (99, -1);
        Assert.Equal(new Cell(64, 0, 30 * BarSizes.MaxDebuff, 20 * BarSizes.MinDebuff), BarSizes.DebuffBoxes(d, 30, 20, 2)[2]);
    }

    /// <summary>A debuff box dragged by its corner in the 레이아웃 편집기: its size grows by as much as the corner moved, as a share
    /// of what is drawn, kept to what the sliders offer.</summary>
    [Fact]
    public void A_corner_drag_resizes_a_debuff_box()
    {
        Assert.Equal((1.5, 2.0), BarSizes.DebuffResized((1, 1), drawn: (30, 20), dx: 15, dy: 20));
        Assert.Equal((BarSizes.MaxDebuff, BarSizes.MinDebuff), BarSizes.DebuffResized((2, 1), drawn: (60, 20), dx: 900, dy: -19));
    }
}
