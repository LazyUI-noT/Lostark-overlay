using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Tests;

public class GaugePaceTests
{
    static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Without_a_press_the_gauge_is_read_every_100_ms() =>
        Assert.Equal(Ms(100), GaugePace.Every(Ms(5000), null));

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(99)]
    public void For_a_tenth_of_a_second_after_a_skill_press_it_is_read_every_frame(double since) =>
        Assert.Equal(Ms(16), GaugePace.Every(Ms(5000 + since), Ms(5000)));

    [Fact]
    public void After_that_it_is_back_to_100_ms() =>
        Assert.Equal(Ms(100), GaugePace.Every(Ms(5100), Ms(5000)));
}
