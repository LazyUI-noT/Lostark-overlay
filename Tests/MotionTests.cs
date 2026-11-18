using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public sealed class MotionTests
{
    [Theory]
    [InlineData(true, false, true, true)]     // Windows animates, local, switch on: runs
    [InlineData(false, false, true, false)]   // Windows' 애니메이션 효과 off
    [InlineData(true, true, true, false)]     // remote desktop
    [InlineData(true, false, false, false)]   // the app's switch off
    [InlineData(false, true, false, false)]   // all three off
    public void Runs_OnlyWhenWindowsLocalAndSwitchAllowIt(bool windowsAnimates, bool remote, bool appSwitch, bool expected)
    {
        Assert.Equal(expected, Motion.Runs(windowsAnimates, remote, appSwitch));
    }

    [Theory]
    [InlineData(-0.5, 0)]   // before the start: still the start
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1.5, 1)]    // past the end: stays at the end
    public void Out_HoldsItsEnds(double t, double expected)
    {
        Assert.Equal(expected, Motion.Out(t), 6);
    }

    [Fact]
    public void Out_IsMostlyThereByHalfway()
    {
        // cubic-bezier(0.23, 1, 0.32, 1) at x = 0.5 is about 0.966 (y = 1 - (1 - s)^3, s ≈ 0.677)
        Assert.InRange(Motion.Out(0.5), 0.95, 0.98);
    }

    [Fact]
    public void Out_NeverGoesBack()
    {
        var last = 0.0;
        for (int i = 1; i <= 100; i++)
        {
            var y = Motion.Out(i / 100.0);
            Assert.True(y >= last, $"Out({i / 100.0}) = {y} < {last}");
            last = y;
        }
    }

    [Fact]
    public void NewSettings_HaveAnimationsOn()
    {
        Assert.True(new Settings().Animations);
    }
}
