using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class GaugeAxisTests
{
    static readonly Cell Bar = new(10, 20, 100, 40);

    [Fact]
    public void Span_flat_runs_left_to_right()
    {
        Assert.Equal(new Cell(35, 20, 50, 40), GaugeAxis.Span(Bar, 0.25, 0.75, false));
    }

    [Fact]
    public void Span_upright_fills_from_the_bottom()
    {
        var c = GaugeAxis.Span(Bar, 0, 0.25, true);
        Assert.Equal(new Cell(10, 50, 100, 10), c);
        Assert.Equal(Bar.Bottom, c.Bottom);
    }

    [Fact]
    public void Span_clamps_and_orders()
    {
        Assert.Equal(Bar, GaugeAxis.Span(Bar, -1, 2, false));
        Assert.Equal(Bar, GaugeAxis.Span(Bar, 1.5, -0.5, true));
    }

    [Fact]
    public void Mark_flat_is_a_vertical_line()
    {
        Assert.Equal(new Cell(59, 20, 2, 40), GaugeAxis.Mark(Bar, 0.5, 2, false));
    }

    [Fact]
    public void Mark_upright_is_a_horizontal_line_measured_from_the_bottom()
    {
        Assert.Equal(new Cell(10, 29, 100, 2), GaugeAxis.Mark(Bar, 0.75, 2, true));
    }

    [Fact]
    public void Sweep_flat_moves_left_to_right_cut_to_the_fill()
    {
        Assert.Null(GaugeAxis.Sweep(Bar, 0, false));
        var mid = GaugeAxis.Sweep(Bar, 0.5, false)!.Value;
        Assert.Equal(25, mid.W);           // a quarter of the fill
        Assert.InRange(mid.X, Bar.X, Bar.Right - mid.W);
        Assert.Equal(Bar.H, mid.H);
    }

    [Fact]
    public void Sweep_upright_moves_bottom_to_top()
    {
        var early = GaugeAxis.Sweep(Bar, 0.1, true)!.Value;
        var late = GaugeAxis.Sweep(Bar, 0.9, true)!.Value;
        Assert.True(late.Y < early.Y);
        Assert.Equal(Bar.W, early.W);
        Assert.True(early.Bottom <= Bar.Bottom && late.Y >= Bar.Y);
    }

    [Fact]
    public void Sweep_band_has_a_two_unit_minimum()
    {
        var thin = new Cell(0, 0, 4, 40);
        Assert.Equal(2, GaugeAxis.Sweep(thin, 0.5, false)!.Value.W);
    }
}
