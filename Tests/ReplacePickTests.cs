using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public sealed class ReplacePickTests
{
    [Fact]
    public void Shows_nothing_until_both_are_picked()
    {
        Assert.Null(new ReplacePick().NumberOnShot);
        Assert.Null(new ReplacePick(Shot: 3).NumberOnShot);
        var slotOnly = new ReplacePick(Slot: 1);
        Assert.Null(slotOnly.NumberOnShot);
        Assert.Null(slotOnly.DashedSlot);
    }

    [Fact]
    public void Shows_number_and_dash_when_both_are_picked()
    {
        var pick = new ReplacePick(Shot: 0, Slot: 4);
        Assert.True(pick.Ready);
        Assert.Equal(5, pick.NumberOnShot);
        Assert.Equal(4, pick.DashedSlot);
    }
}
