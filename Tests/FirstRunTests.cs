using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class FirstRunTests
{
    [Fact]
    public void Closing_after_a_fit_gives_only_the_tray_fact()
    {
        Assert.Equal(FirstRun.TrayFact, FirstRun.ClosingNotice(fitted: true));
    }

    [Fact]
    public void Closing_without_a_fit_says_how_to_fit_first_then_the_tray_fact()
    {
        string notice = FirstRun.ClosingNotice(fitted: false);

        Assert.StartsWith(FirstRun.FitLater, notice);
        Assert.EndsWith(FirstRun.TrayFact, notice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_notice_fits_a_balloon(bool fitted)
    {
        // Windows cuts balloon text past 255 characters.
        Assert.InRange(FirstRun.ClosingNotice(fitted).Length, 1, 255);
    }
}
