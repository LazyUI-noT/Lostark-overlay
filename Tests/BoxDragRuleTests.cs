using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class BoxDragRuleTests
{
    [Theory]
    [InlineData(true, false, false)]   // 내 스킬창, tick off: the whole window moves
    [InlineData(true, true, true)]     // tick on: the box moves
    [InlineData(false, false, true)]   // 파티원: boxes move as before
    [InlineData(false, true, true)]
    public void A_box_moves_alone_only_off_the_mine_window_or_with_the_tick(bool mine, bool apart, bool movesBox)
    {
        Assert.Equal(movesBox, BoxDragRule.MovesBox(mine, apart));
    }
}
