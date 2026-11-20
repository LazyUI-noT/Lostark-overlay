using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>빈 칸 줄이기 (slot-layout.plan.md §1): where each shown box sits in 칸 맞춤, boxes 34 × 34, gap 2.</summary>
public class SlotGridTests
{
    const double B = 34, G = 2;

    static Dictionary<SlotId, Cell> Arrange(bool closeGaps, params SlotId[] hidden) =>
        Arrange(closeGaps, _ => true, hidden);

    static Dictionary<SlotId, Cell> Arrange(bool closeGaps, Func<SlotId, bool> present, params SlotId[] hidden)
    {
        var placed = SlotGrid.Arrange(SlotGrid.Default, present, hidden.ToHashSet(), closeGaps, B, B, G);
        return placed.Boxes.Where(b => !b.Hidden).ToDictionary(b => b.Slot, b => b.Cell);
    }

    static double Width(bool closeGaps, params SlotId[] hidden) =>
        SlotGrid.Arrange(SlotGrid.Default, _ => true, hidden.ToHashSet(), closeGaps, B, B, G).Width;

    [Fact]
    public void All_shown_is_the_fixed_grid()
    {
        var c = Arrange(true);

        Assert.Equal(new Cell(0, 0, 34, 70), c[SlotId.각성기]);
        Assert.Equal(new Cell(36, 0, 34, 70), c[SlotId.초각스]);
        Assert.Equal(new Cell(72, 0, 34, 34), c[SlotId.일반1]);
        Assert.Equal(new Cell(72 + 3 * 36, 36, 34, 34), c[SlotId.일반8]);
        Assert.Equal(new Cell(216, 0, 34, 34), c[SlotId.아덴1]);
        Assert.Equal(new Cell(216, 36, 34, 34), c[SlotId.아덴2]);
        Assert.Equal(250, Width(true));
    }

    [Fact]
    public void Q_off_widens_the_rest_of_its_row_and_keeps_the_panel()
    {
        var c = Arrange(true, SlotId.일반1);

        Assert.DoesNotContain(SlotId.일반1, c.Keys);
        Assert.Equal(new Cell(72, 0, 46, 34), c[SlotId.일반2]);
        Assert.Equal(new Cell(120, 0, 46, 34), c[SlotId.일반3]);
        Assert.Equal(new Cell(168, 0, 46, 34), c[SlotId.일반4]);
        Assert.Equal(new Cell(72, 36, 34, 34), c[SlotId.일반5]);
        Assert.Equal(250, Width(true, SlotId.일반1));
    }

    [Fact]
    public void W_and_D_off_lose_a_column_with_a_wide_gap_where_each_left()
    {
        var c = Arrange(true, SlotId.일반2, SlotId.일반7);

        Assert.Equal(new Cell(72, 0, 34, 34), c[SlotId.일반1]);
        Assert.Equal(new Cell(112, 0, 34, 34), c[SlotId.일반3]);
        Assert.Equal(new Cell(148, 0, 34, 34), c[SlotId.일반4]);
        Assert.Equal(new Cell(72, 36, 34, 34), c[SlotId.일반5]);
        Assert.Equal(new Cell(108, 36, 34, 34), c[SlotId.일반6]);
        Assert.Equal(new Cell(148, 36, 34, 34), c[SlotId.일반8]);
        Assert.Equal(new Cell(184, 0, 34, 34), c[SlotId.아덴1]);
    }

    [Fact]
    public void Q_and_A_off_remove_the_column_with_no_wide_gap()
    {
        var c = Arrange(true, SlotId.일반1, SlotId.일반5);

        Assert.Equal(new Cell(72, 0, 34, 34), c[SlotId.일반2]);
        Assert.Equal(new Cell(144, 36, 34, 34), c[SlotId.일반8]);
        Assert.Equal(new Cell(180, 0, 34, 34), c[SlotId.아덴1]);
        Assert.Equal(214, Width(true, SlotId.일반1, SlotId.일반5));
    }

    [Fact]
    public void Q_W_and_D_off_let_row_two_set_the_width()
    {
        var c = Arrange(true, SlotId.일반1, SlotId.일반2, SlotId.일반7);

        Assert.Equal(new Cell(72, 36, 34, 34), c[SlotId.일반5]);
        Assert.Equal(new Cell(148, 36, 34, 34), c[SlotId.일반8]);
        Assert.Equal(new Cell(72, 0, 54, 34), c[SlotId.일반3]);
        Assert.Equal(new Cell(128, 0, 54, 34), c[SlotId.일반4]);
    }

    [Fact]
    public void Switch_off_keeps_every_spot()
    {
        var placed = SlotGrid.Arrange(SlotGrid.Default, _ => true, new HashSet<SlotId> { SlotId.일반1, SlotId.각성기 }, false, B, B, G);
        var c = placed.Boxes.ToDictionary(b => b.Slot);

        Assert.True(c[SlotId.일반1].Hidden);
        Assert.Equal(new Cell(72, 0, 34, 34), c[SlotId.일반1].Cell);
        Assert.Equal(new Cell(108, 0, 34, 34), c[SlotId.일반2].Cell);
        Assert.True(c[SlotId.각성기].Hidden);   // its column stays, for the 변화 strip
        Assert.Equal(250, placed.Width);
    }

    [Fact]
    public void A_key_run_with_one_row_left_fills_the_column_height()
    {
        var c = Arrange(true, SlotId.일반5, SlotId.일반6, SlotId.일반7, SlotId.일반8);

        Assert.Equal(new Cell(72, 0, 34, 70), c[SlotId.일반1]);
        Assert.Equal(new Cell(180, 0, 34, 70), c[SlotId.일반4]);
    }

    [Fact]
    public void An_absent_group_takes_no_column()
    {
        var c = Arrange(true, s => s != SlotId.초각스);

        Assert.Equal(new Cell(36, 0, 34, 34), c[SlotId.일반1]);
    }

    [Fact]
    public void One_aden_box_hidden_stretches_the_other_to_full_height()
    {
        var c = Arrange(true, SlotId.아덴1);

        Assert.Equal(new Cell(216, 0, 34, 70), c[SlotId.아덴2]);
    }

    [Fact]
    public void A_hidden_stack_block_removes_its_column()
    {
        var c = Arrange(true, SlotId.각성기);

        Assert.Equal(new Cell(0, 0, 34, 70), c[SlotId.초각스]);
        Assert.Equal(214, Width(true, SlotId.각성기));
    }

    [Fact]
    public void Nothing_shown_is_empty()
    {
        var placed = SlotGrid.Arrange(SlotGrid.Default, _ => false, new HashSet<SlotId>(), true, B, B, G);

        Assert.Empty(placed.Boxes);
        Assert.Equal(0, placed.Width);
    }

    static Dictionary<BuffKind, Cell> Bars(bool closeGaps, params BuffKind[] off) =>
        BarRows.Arrange(BarRows.Default, k => !off.Contains(k), closeGaps, 250, G, _ => 12, _ => 1)
            .ToDictionary(b => b.Kind, b => b.Cell);

    [Fact]
    public void All_bars_two_to_a_row_and_the_lone_last_one_widens()
    {
        var c = Bars(true);

        Assert.Equal(new Cell(0, 0, 124, 12), c[BuffKind.공증]);
        Assert.Equal(new Cell(126, 0, 124, 12), c[BuffKind.낙인]);
        Assert.Equal(new Cell(0, 14, 124, 12), c[BuffKind.아덴피증]);
        Assert.Equal(new Cell(0, 28, 250, 12), c[BuffKind.뎀증]);
    }

    [Fact]
    public void A_bar_whose_row_mate_is_off_widens_and_keeps_its_row()
    {
        var c = Bars(true, BuffKind.공증);

        Assert.Equal(new Cell(0, 0, 250, 12), c[BuffKind.낙인]);
        Assert.Equal(new Cell(0, 14, 124, 12), c[BuffKind.아덴피증]);
    }

    [Fact]
    public void An_empty_bar_row_closes_up()
    {
        var c = Bars(true, BuffKind.공증, BuffKind.낙인);

        Assert.Equal(new Cell(0, 0, 124, 12), c[BuffKind.아덴피증]);
        Assert.Equal(new Cell(0, 14, 250, 12), c[BuffKind.뎀증]);
    }

    [Fact]
    public void Switch_off_keeps_todays_flow_two_to_a_row_half_wide()
    {
        var c = Bars(false, BuffKind.공증);

        Assert.Equal(new Cell(0, 0, 124, 12), c[BuffKind.낙인]);
        Assert.Equal(new Cell(126, 0, 124, 12), c[BuffKind.아덴피증]);
        Assert.Equal(new Cell(0, 14, 124, 12), c[BuffKind.초각스피증]);
        Assert.Equal(new Cell(126, 14, 124, 12), c[BuffKind.뎀증]);
    }

    [Fact]
    public void A_row_is_as_tall_as_its_thicker_bar_and_length_shortens_a_bar()
    {
        var bars = BarRows.Arrange(BarRows.Default, _ => true, true, 250, G,
            k => k == BuffKind.공증 ? 24 : 12, k => k == BuffKind.낙인 ? 0.5 : 1).ToDictionary(b => b.Kind, b => b.Cell);

        Assert.Equal(new Cell(126, 0, 62, 12), bars[BuffKind.낙인]);
        Assert.Equal(26, bars[BuffKind.아덴피증].Y);
    }
}
