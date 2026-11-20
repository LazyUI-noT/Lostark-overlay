using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class DropMarkerTests
{
    static readonly Cell Block = new(10, 20, 40, 30);

    [Fact]
    public void Middle_is_the_block_itself() => Assert.Equal(Block, DropMarker.Of(Block, DropSide.Middle, 4));

    [Theory]
    [InlineData(DropSide.Left, 6.5, 20, 3, 30)]
    [InlineData(DropSide.Right, 50.5, 20, 3, 30)]
    [InlineData(DropSide.Top, 10, 16.5, 40, 3)]
    [InlineData(DropSide.Bottom, 10, 50.5, 40, 3)]
    public void A_side_is_a_line_centred_in_the_gap(DropSide side, double x, double y, double w, double h)
    {
        var c = DropMarker.Of(Block, side, 4);
        Assert.Equal((x, y, w, h), (c.X, c.Y, c.W, c.H));
    }

    [Fact]
    public void A_thicker_line_stays_centred_in_the_gap()
    {
        var c = DropMarker.Of(Block, DropSide.Left, 4, 6);
        Assert.Equal((5.0, 6.0), (c.X, c.W));
        Assert.Equal(c.X + c.W / 2, Block.X - 2);
    }
}
