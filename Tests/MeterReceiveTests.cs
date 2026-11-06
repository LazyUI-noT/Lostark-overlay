using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>파티 → DPS 미터기 (파티원 전분): only someone who sends receives, and receiving can be cut on its own.</summary>
public class MeterReceiveTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void The_party_figures_need_sending_and_receiving(bool send, bool receive, bool takes) =>
        Assert.Equal(takes, new MeterSettings { Send = send, Receive = receive }.TakesParty);

    [Fact]
    public void Receiving_is_on_by_default() => Assert.True(new MeterSettings().Receive);
}
