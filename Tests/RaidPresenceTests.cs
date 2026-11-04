using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Where the player is, from what the game shows: the in-game DPS meter (전분) shows from a raid's entry (timer 0), the
/// boss HP bar only after the first hit. Raid reads wait for these (meter-gating.plan.md).</summary>
public sealed class RaidPresenceTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Nothing_seen_is_not_in_a_dungeon()
    {
        Assert.False(RaidPresence.None.InDungeon(S(100)));
        Assert.False(RaidPresence.None.BossUp(S(100)));
    }

    [Fact]
    public void The_meter_alone_is_a_dungeon_without_a_boss()
    {
        var p = RaidPresence.None.Saw(meter: true, boss: false, S(10));
        Assert.True(p.InDungeon(S(10)));
        Assert.False(p.BossUp(S(10)));
    }

    [Fact]
    public void A_boss_bar_alone_is_a_dungeon_too()
    {
        // a player who hides the in-game meter: the boss bar starts everything
        var p = RaidPresence.None.Saw(meter: false, boss: true, S(10));
        Assert.True(p.InDungeon(S(10)));
        Assert.True(p.BossUp(S(10)));
    }

    [Fact]
    public void The_dungeon_holds_through_a_cutscene_then_ends()
    {
        var p = RaidPresence.None.Saw(true, false, S(10));
        Assert.True(p.InDungeon(S(10) + RaidPresence.Hold));
        Assert.False(p.InDungeon(S(10) + RaidPresence.Hold + S(1)));
    }

    [Fact]
    public void The_boss_is_up_only_shortly_after_its_bar_was_read()
    {
        var p = RaidPresence.None.Saw(false, true, S(10));
        Assert.True(p.BossUp(S(10) + RaidPresence.BossRecent));
        Assert.False(p.BossUp(S(10) + RaidPresence.BossRecent + S(1)));
    }

    [Fact]
    public void Seeing_nothing_keeps_the_last_sightings()
    {
        var p = RaidPresence.None.Saw(true, true, S(10)).Saw(false, false, S(20));
        Assert.Equal(S(10), p.MeterSeen);
        Assert.Equal(S(10), p.BossSeen);
    }

    [Fact]
    public void A_stay_starts_when_first_seen_and_again_after_a_gap()
    {
        var p = RaidPresence.None.Saw(true, false, S(10)).Saw(true, false, S(30));
        Assert.Equal(S(10), p.Since);
        var later = p.Saw(true, false, S(30) + RaidPresence.Hold + S(5));   // left, came back: another stay
        Assert.Equal(S(30) + RaidPresence.Hold + S(5), later.Since);
    }

    // Cutscenes hide the HUD and the in-game meter (user, 2026-11-03): a hidden HUD inside a dungeon holds it, however long.
    [Fact]
    public void A_hidden_hud_in_a_dungeon_holds_it_through_a_long_cutscene()
    {
        var p = RaidPresence.None.Saw(true, true, S(10));
        for (int t = 11; t <= 190; t++) p = p.Hidden(S(t));   // a 3-minute cutscene
        Assert.True(p.InDungeon(S(190) + RaidPresence.Hold));
        Assert.Equal(S(10), p.Since);   // the same stay: the meter window keeps showing
        Assert.False(p.BossUp(S(190)));
    }

    [Fact]
    public void A_hidden_hud_outside_a_dungeon_starts_nothing()
    {
        var p = RaidPresence.None.Hidden(S(10));
        Assert.False(p.InDungeon(S(10)));
    }

    [Fact]
    public void A_hidden_hud_holds_at_most_a_while_after_the_last_real_sighting()
    {
        // a menu or character select covering the skill bar must not keep a dungeon forever (review, 2026-11-03)
        var p = RaidPresence.None.Saw(true, false, S(10));
        var end = S(10) + RaidPresence.MaxHidden;
        for (int t = 11; t <= (int)end.TotalSeconds + 120; t++) p = p.Hidden(S(t));
        Assert.False(p.InDungeon(end + RaidPresence.Hold + S(1)));
    }

    [Fact]
    public void Another_app_in_front_holds_a_dungeon_however_long_and_the_stay_goes_on()
    {
        var p = RaidPresence.None.Saw(true, true, S(10));
        for (int t = 11; t <= 1210; t++) p = p.Away(S(t));   // 20 minutes on a video alt-tabbed to
        Assert.True(p.InDungeon(S(1210) + RaidPresence.Hold));
        Assert.Equal(S(10), p.Since);
    }

    [Fact]
    public void Away_from_the_game_outside_a_dungeon_is_nowhere() =>
        Assert.False(RaidPresence.None.Away(S(10)).InDungeon(S(10)));
}

