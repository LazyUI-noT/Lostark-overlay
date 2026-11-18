using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public sealed class MotionFramesTests
{
    [Fact]
    public void Durations_AreTheGapsToTheNextFrame_AndTheTailForTheLast()
    {
        Assert.Equal([30, 20, 50], MotionFrames.Durations([-30, 0, 20], 50));
    }

    [Fact]
    public void ConcatList_ScalesTheGaps_AndRepeatsTheLastFile()
    {
        var text = MotionFrames.ConcatList(["a.png", "b.png"], [0, 100], slow: 4, tailMs: 50);
        Assert.Equal("file 'a.png'\nduration 0.4\nfile 'b.png'\nduration 0.2\nfile 'b.png'\n", text);
    }

    [Fact]
    public void ConcatList_NoFrames_IsEmpty()
    {
        Assert.Equal("", MotionFrames.ConcatList([], [], 1, 50));
    }

    [Fact]
    public void Nearest_PicksTheClosestStamp_TheEarlierOnATie()
    {
        Assert.Equal([0, 1, 1, 2], MotionFrames.Nearest([-20, 10, 40], [-100, 20, 25, 90]));
    }

    [Fact]
    public void Nearest_NoFrames_IsEmpty()
    {
        Assert.Empty(MotionFrames.Nearest([], [0, 1]));
    }
}
