using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class TrayTipTests
{
    [Fact]
    public void Names_the_profile_alone_while_the_game_runs_without_a_party() =>
        Assert.Equal("GaugeOverlay · 프로필 2", TrayTip.Text(profile: 2, gameRunning: true, partyActive: false));

    [Fact]
    public void Says_the_game_is_off_as_the_dimmed_icon_does() =>
        Assert.Equal("GaugeOverlay · 프로필 1 · 게임 꺼짐", TrayTip.Text(1, gameRunning: false, partyActive: false));

    [Fact]
    public void Says_party_sharing_as_the_green_dot_does() =>
        Assert.Equal("GaugeOverlay · 프로필 1 · 게임 꺼짐 · 파티 공유 중", TrayTip.Text(1, gameRunning: false, partyActive: true));

    [Fact]
    public void Fits_the_notify_icon_limit() =>
        Assert.True(TrayTip.Text(OverlayProfile.Count, false, true).Length < 128);
}
