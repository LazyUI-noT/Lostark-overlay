using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class HudVoteTests
{
    // A fit taken with most slots cooling leaves fewer than MinSlots references: no frame can vote, so saving only on a
    // "shown" vote would never add the missing ones and the HUD would stay unknown (buffs, gauge, debuffs frozen).
    [Theory]
    [InlineData(true, 12, 12, 0, true)]     // the vote says shown
    [InlineData(null, 3, 3, 0, true)]       // too few references to vote, all of them readable: the bar is there
    [InlineData(null, 3, 2, 0, true)]       // most of them read
    [InlineData(null, 3, 1, 0, false)]      // one of three read: too little to vouch for the bar
    [InlineData(null, 1, 1, 0, true)]
    [InlineData(null, 3, 3, 1, false)]      // one reference unreadable: maybe a loading screen
    [InlineData(null, 3, 0, 0, false)]      // no reference read this frame: nothing vouches for the bar
    [InlineData(null, 0, 0, 0, false)]      // no references at all
    [InlineData(null, 6, 2, 0, false)]      // enough references exist: wait for a real vote
    [InlineData(false, 3, 3, 0, false)]
    public void Saves_new_references_only_when_the_bar_is_vouched_for(bool? shown, int stored, int calibrated, int unreadable, bool save) =>
        Assert.Equal(save, HudVote.MaySave(shown, stored, calibrated, unreadable));

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
