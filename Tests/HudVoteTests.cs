using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class HudVoteTests
{
    [Fact]
    public void Frames_with_few_calibrated_slots_never_hide_the_HUD()
    {
        var hud = new HudVote();
        for (int i = 0; i < 10; i++) Assert.Null(hud.Add(calibrated: 1, unreadable: 1));   // one covered slot per frame
        Assert.Null(hud.Visible);
    }

    [Fact]
    public void Starts_unknown_and_three_agreeing_votes_decide()
    {
        var hud = new HudVote();
        Assert.Null(hud.Visible);
        Assert.True(hud.Add(12, 0));
        Assert.True(hud.Add(12, 1));
        Assert.Null(hud.Visible);
        Assert.True(hud.Add(12, 7));
        Assert.True(hud.Visible);

        Assert.False(hud.Add(12, 12));
        Assert.False(hud.Add(12, 8));
        Assert.Null(hud.Add(3, 3));   // doesn't vote, doesn't break the run
        Assert.True(hud.Visible);
        Assert.False(hud.Add(4, 3));
        Assert.False(hud.Visible);
    }
}
