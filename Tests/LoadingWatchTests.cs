using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>What a loading screen means for where the player is (meter-gating.plan.md phase 2, user 2026-11-04): two bar reads
/// in a row while the HUD is hidden make a loading; after it, a stay the meter showed ends when no meter or boss bar comes back
/// within 5 s of the HUD.</summary>
public class LoadingWatchTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    // In a dungeon by the meter at 10 s, the HUD hidden from 20 s: bar reads at 20.0 and 20.25.
    static readonly RaidPresence ByMeter = RaidPresence.None.Saw(meter: true, boss: false, S(10)).Saw(true, false, S(19));

    [Fact]
    public void One_bar_read_is_no_loading_two_in_a_row_are()
    {
        var w = LoadingWatch.None.Read(true, ByMeter, S(20));
        Assert.False(w.Loading);
        Assert.True(w.Read(true, ByMeter, S(20.25)).Loading);
        Assert.False(w.Read(false, ByMeter, S(20.25)).Read(true, ByMeter, S(20.5)).Loading);   // a miss starts over
    }

    [Fact]
    public void A_stay_the_meter_showed_ends_5_s_after_the_HUD_is_back_without_it()
    {
        var w = LoadingWatch.None.Read(true, ByMeter, S(20)).Read(true, ByMeter, S(20.25)).HudBack(S(26));
        Assert.False(Run(w, 26, 30.9, counting: true).Left);
        Assert.True(Run(w, 26, 31, counting: true).Left);
    }

    [Fact]
    public void The_meter_or_a_boss_bar_after_the_loading_keeps_the_stay()
    {
        var w = LoadingWatch.None.Read(true, ByMeter, S(20)).Read(true, ByMeter, S(20.25)).HudBack(S(26)).Saw();
        Assert.False(Run(w, 26, 40, counting: true).Left);
        Assert.Equal(LoadingWatch.None, w);
    }

    [Fact]
    public void A_stay_shown_by_a_boss_bar_alone_keeps_the_60_s_hold()
    {
        var byBoss = RaidPresence.None.Saw(meter: false, boss: true, S(10));   // a player who hides the meter
        var w = LoadingWatch.None.Read(true, byBoss, S(20)).Read(true, byBoss, S(20.25)).HudBack(S(26));
        Assert.True(w.Loading is false && w.WaitFrom is not null);
        Assert.False(Run(w, 26, 60, counting: true).Left);
    }

    [Fact]
    public void Outside_a_dungeon_a_loading_has_nothing_to_end()
    {
        var w = LoadingWatch.None.Read(true, RaidPresence.None, S(20)).Read(true, RaidPresence.None, S(20.25)).HudBack(S(26));
        Assert.False(Run(w, 26, 60, counting: true).Left);
    }

    [Fact]
    public void A_cutscene_without_the_bar_starts_no_wait()
    {
        var w = LoadingWatch.None.Read(false, ByMeter, S(20)).HudBack(S(26));
        Assert.Null(w.WaitFrom);
        Assert.False(Run(w, 26, 60, counting: true).Left);
    }

    [Fact]
    public void A_stay_from_before_the_meter_was_seen_again_is_still_the_meters()
    {
        // The meter seen at the stay's start only (cutscene before the loading): it still showed this stay.
        var p = RaidPresence.None.Saw(true, false, S(10)).Hidden(S(30));
        var w = LoadingWatch.None.Read(true, p, S(40)).Read(true, p, S(40.25)).HudBack(S(45));
        Assert.True(Run(w, 45, 50, counting: true).Left);
    }

    // Review HIGH (2026-11-04): an intro cutscene right after the loading hides the HUD and the meter for long; the wait must
    // not run through it. Nor while the game is behind another window (alt-tab).
    [Fact]
    public void The_wait_pauses_while_the_HUD_is_hidden_again_or_the_game_is_not_in_front()
    {
        var w = LoadingWatch.None.Read(true, ByMeter, S(20)).Read(true, ByMeter, S(20.25)).HudBack(S(26));
        w = Run(w, 26, 28, counting: true);      // 2 s with the HUD up
        w = Run(w, 28, 60, counting: false);     // a 32 s cutscene
        Assert.False(w.Left);
        Assert.False(Run(w, 60, 62.9, counting: true).Left);
        Assert.True(Run(w, 60, 63, counting: true).Left);   // 2 + 3 s counted
    }

    /// <summary>Steps of 0.05 s from <paramref name="from"/> to <paramref name="to"/>, all counting or all not.</summary>
    static LoadingWatch Run(LoadingWatch w, double from, double to, bool counting)
    {
        for (double t = from; t <= to + 1e-9; t += 0.05) w = w.Tick(counting, S(t));
        return w.Tick(counting, S(to));
    }
}
