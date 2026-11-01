using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Round-number grids on the DPS graphs (records-timeline.design.md): 3–5 lines at 1, 2, 2.5, 5 × 10ⁿ, the top raised to
/// the first line at or above the peak; time lines as fine as the labels allow.</summary>
public class GridScaleTests
{
    [Theory]
    [InlineData(1.37e9, 5e8, 1.5e9)]      // 13.7억/초 → 5억 steps to 15억 (3 lines)
    [InlineData(2.9e9, 1e9, 3e9)]         // 29억 → 10억 steps to 30억
    [InlineData(4.1e9, 1e9, 5e9)]         // 41억 → 10억 steps to 50억 (5 lines), not 20억 steps to 60억
    [InlineData(7.2e11, 2.5e11, 7.5e11)]  // 피해량 7200억 → 2500억 steps to 7500억 (3)
    [InlineData(1e9, 2.5e8, 1e9)]         // exactly 10억 → 2.5억 × 4 (a tie with 2억 × 5 goes to the larger step)
    public void Values_pick_a_round_step_with_three_to_five_lines(double max, double step, double top)
    {
        var (s, t) = GridScale.Values(max);
        Assert.Equal(step, s, 6);
        Assert.Equal(top, t, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void An_empty_graph_gets_one_line(double max) => Assert.Equal((1e8, 1e8), GridScale.Values(max));

    [Theory]
    [InlineData(600, 700, 70, 60)]   // 10 min across 700 px, labels ≥ 70 px apart → 1:00
    [InlineData(120, 300, 50, 30)]
    [InlineData(20, 700, 70, 2)]
    [InlineData(36000, 300, 70, 1200)]   // nothing fits: the widest step
    public void Seconds_pick_the_finest_step_that_keeps_labels_apart(double span, double width, double gap, double step) =>
        Assert.Equal(step, GridScale.Seconds(span, width, gap));

    [Theory]
    [InlineData(100, 300, 12, 60, true)]    // a fifth of the width for the labels
    [InlineData(40, 300, 12, 60, false)]    // shorter than four lines of text
    [InlineData(100, 150, 12, 60, false)]   // the labels take 40 % of the width
    [InlineData(48, 144, 12, 48, true)]     // exactly four lines tall, and the labels exactly a third
    public void Labels_are_drawn_only_where_the_graph_has_room(double height, double width, double font, double labelWidth, bool fits) =>
        Assert.Equal(fits, GridScale.LabelsFit(height, width, font, labelWidth));

    [Theory]
    [InlineData(5e8, "5억")]
    [InlineData(2.5e8, "2.5억")]
    [InlineData(1e12, "1조")]
    [InlineData(8e11, "8000억")]
    [InlineData(0, "0")]
    public void Labels_read_in_the_game_units_without_a_needless_decimal(double value, string label) =>
        Assert.Equal(label, GridScale.Label(value));
}
