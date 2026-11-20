using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 따로 buff window in 칸 맞춤 (slot-layout.plan.md §4): one column of bars (one row when 세로) in the saved
/// arrangement, and its drops.</summary>
public class BuffStackTests
{
    const BuffKind 공 = BuffKind.공증, 아 = BuffKind.아덴피증, 초 = BuffKind.초각스피증, 뎀 = BuffKind.뎀증, 낙 = BuffKind.낙인;
    static readonly IReadOnlyList<BuffKind> All = BarRows.Default.SelectMany(r => r).ToList();   // 공 낙 아 초 뎀

    static (IReadOnlyList<(BuffKind Kind, Cell Cell)> Bars, double Width, double Height) Flat(IReadOnlyList<BuffKind> order, double length = 1,
        Func<BuffKind, double>? thickness = null) => BuffStack.Arrange(order, false, 100, length, 2, thickness ?? (_ => 10));

    [Fact]
    public void The_order_is_the_arrangement_read_row_by_row_without_hidden_bars()
    {
        Assert.Equal([공, 낙, 아, 초, 뎀], BuffStack.Order(BarRows.Default, _ => true));
        Assert.Equal([공, 아, 뎀], BuffStack.Order(BarRows.Default, k => k is 공 or 아 or 뎀));
        Assert.Equal([뎀, 공, 낙, 아, 초], BuffStack.Order(BarArrangement.Of("3/0,4/1,2"), _ => true));
    }

    [Fact]
    public void Flat_bars_stack_in_one_column_the_window_as_wide_as_given()
    {
        var (bars, w, h) = Flat([공, 낙, 아]);
        Assert.Equal([new Cell(0, 0, 100, 10), new Cell(0, 12, 100, 10), new Cell(0, 24, 100, 10)], bars.Select(b => b.Cell));
        Assert.Equal((100, 34), (w, h));
    }

    [Fact]
    public void A_hidden_bar_shrinks_the_window_by_its_share_and_no_bar_gets_thicker()
    {
        var all = Flat(All);
        var without = Flat([공, 낙, 초, 뎀]);   // 아덴 off
        Assert.Equal(all.Height - 12, without.Height);
        Assert.All(without.Bars, b => Assert.Equal(10, b.Cell.H));
    }

    [Fact]
    public void Thickness_two_is_twice_a_thickness_one_bar()
    {
        var (bars, _, h) = Flat([공, 낙], thickness: k => k == 공 ? 20 : 10);
        Assert.Equal([20.0, 10.0], bars.Select(b => b.Cell.H));
        Assert.Equal(32, h);
    }

    [Fact]
    public void The_shared_length_shortens_every_bar_but_not_the_window()
    {
        var (bars, w, _) = Flat([공, 낙], length: 0.5);
        Assert.Equal([50.0, 50.0], bars.Select(b => b.Cell.W));
        Assert.Equal(100, w);
    }

    [Fact]
    public void Upright_bars_stand_side_by_side_the_first_on_the_right_on_the_bottom_edge()
    {
        var (bars, w, h) = BuffStack.Arrange([공, 낙], true, 100, 0.6, 2, k => k == 공 ? 8 : 6);
        Assert.Equal(new Cell(8, 40, 8, 60), bars.Single(b => b.Kind == 공).Cell);
        Assert.Equal(new Cell(0, 40, 6, 60), bars.Single(b => b.Kind == 낙).Cell);
        Assert.Equal((16, 100), (w, h));
    }

    [Fact]
    public void Nothing_shown_is_an_empty_window()
    {
        var (bars, w, h) = Flat([]);
        Assert.Empty(bars);
        Assert.Equal((0, 0), (w, h));
    }

    static IReadOnlyList<(BuffKind Kind, Cell Cell)> Cells() => Flat([공, 낙, 아]).Bars;   // y 0, 12, 24; 10 tall

    [Fact]
    public void A_drop_on_a_bars_middle_swaps_and_near_its_ends_inserts()
    {
        Assert.Equal(new BarTarget(낙, DropSide.Middle), BuffStackDrop.TargetAt(공, Cells(), false, 50, 17));
        Assert.Equal(new BarTarget(낙, DropSide.Top), BuffStackDrop.TargetAt(공, Cells(), false, 50, 12.5));
        Assert.Equal(new BarTarget(낙, DropSide.Bottom), BuffStackDrop.TargetAt(공, Cells(), false, 50, 21));
        Assert.Null(BuffStackDrop.TargetAt(공, Cells(), false, 50, 5));    // over itself
        Assert.Null(BuffStackDrop.TargetAt(공, Cells(), false, 150, 17));  // beside the window
    }

    [Fact]
    public void An_upright_bar_inserts_by_its_sides()
    {
        var (bars, _, _) = BuffStack.Arrange([공, 낙], true, 100, 1, 2, _ => 10);   // 낙 at x 0, 공 at x 12
        Assert.Equal(new BarTarget(공, DropSide.Right), BuffStackDrop.TargetAt(낙, bars, true, 21, 50));
        Assert.Equal(new BarTarget(공, DropSide.Left), BuffStackDrop.TargetAt(낙, bars, true, 12.5, 50));
    }

    [Theory]
    [InlineData(공, 아, DropSide.Middle, "1,4/0,2/3")]   // swap 공 ↔ 아: shape of the rows stays
    [InlineData(뎀, 공, DropSide.Top, "3,0/4,1/2")]       // 뎀 before 공 → 뎀 공 낙 아 초 refilled 2/2/1
    [InlineData(공, 뎀, DropSide.Bottom, "4,1/2,3/0")]
    public void A_drop_reorders_the_bars_and_keeps_the_rows_shape(BuffKind dragged, BuffKind anchor, DropSide side, string expected)
    {
        // start "0,4/1,2/3" = 공 낙 / 아 초 / 뎀 → flat 0 4 1 2 3
        var rows = BuffStackDrop.Apply(BarRows.Default, dragged, new BarTarget(anchor, side), false);
        Assert.Equal(expected, BarArrangement.Format(rows));
    }

    [Fact]
    public void Upright_before_means_to_the_right()
    {
        var rows = BuffStackDrop.Apply(BarRows.Default, 뎀, new BarTarget(공, DropSide.Right), true);   // right of 공 = before it
        Assert.Equal("3,0/4,1/2", BarArrangement.Format(rows));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0.5, 0.5)]
    [InlineData(0, BarSizes.MinLength)]
    [InlineData(7, 1)]
    [InlineData(double.NaN, 1)]
    public void The_shared_length_is_clamped(double saved, double expected) =>
        Assert.Equal(expected, BarSizes.Shared(new BuffSettings { SharedLength = saved }));

    [Fact]
    public void Each_bars_own_length_is_kept_apart_from_the_shared_one()
    {
        var b = new BuffSettings { SharedLength = 0.4 };
        b.Edit(공).Length = 0.7;
        Assert.Equal(0.4, BarSizes.Shared(b));
        Assert.Equal(0.7, BarSizes.Length(b.Of(공)));
    }
}
