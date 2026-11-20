using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class BoxDragRuleTests
{
    [Theory]
    [InlineData(true, false, false)]   // 내 스킬창 in 칸 맞춤: the whole window moves
    [InlineData(true, true, true)]     // 자유 배치: the box moves
    [InlineData(false, false, true)]   // 파티원: boxes move as before
    [InlineData(false, true, true)]
    public void A_box_moves_alone_only_off_the_mine_window_or_in_free_placement(bool mine, bool free, bool movesBox)
    {
        Assert.Equal(movesBox, BoxDragRule.MovesBox(mine, free));
    }
}
