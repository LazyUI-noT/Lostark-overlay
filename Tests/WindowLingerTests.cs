using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

// The DPS 미터기 and the 줄 수 창 read all the time but show only while a boss is read, and 10 s after (user, 2026-11-01).
public sealed class WindowLingerTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Theory]
    [InlineData(null, 5.0, false)]   // never read
    [InlineData(5.0, 5.0, true)]     // read now
    [InlineData(5.0, 15.0, true)]    // 10 s after: still up (a cutscene, a phase change)
    [InlineData(5.0, 15.1, false)]   // later: hidden
    public void A_window_shows_from_its_read_until_ten_seconds_after(double? read, double now, bool shown) =>
        Assert.Equal(shown, WindowLinger.InUse(read is { } r ? S(r) : null, S(now)));

    [Fact]
    public void A_read_now_moves_the_last_read()
    {
        Assert.Equal(S(9), WindowLinger.Seen(S(2), readNow: true, S(9)));
        Assert.Equal(S(2), WindowLinger.Seen(S(2), readNow: false, S(9)));
        Assert.Null(WindowLinger.Seen(null, readNow: false, S(9)));
    }

    [Fact]
    public void The_meter_was_last_read_at_its_later_read()
    {
        var status = MeterStatus.None with { LastRead = S(3), HpAt = S(7) };
        Assert.Equal(S(7), WindowLinger.LastRead(status));
        Assert.Null(WindowLinger.LastRead(MeterStatus.None));   // nothing read yet
        Assert.Null(WindowLinger.LastRead(null));
    }

    // The DPS 미터기 window: shows from the first hit (a boss read), then stays while the player is in that dungeon (the in-game
    // meter or a boss bar seen in RaidPresence.Hold), not only 10 s (meter-gating.plan.md).
    [Fact]
    public void The_meter_window_waits_for_the_first_hit()
    {
        var inside = RaidPresence.None.Saw(meter: true, boss: false, S(10));
        Assert.False(WindowLinger.MeterInUse(lastRead: null, inside, S(12)));
    }

    [Fact]
    public void The_meter_window_stays_while_in_the_dungeon_of_its_read()
    {
        var p = RaidPresence.None.Saw(true, false, S(10)).Saw(true, true, S(20)).Saw(true, false, S(80));
        Assert.True(WindowLinger.MeterInUse(lastRead: S(20), p, S(80)));   // 60 s after the last boss read: the meter still shows
        Assert.False(WindowLinger.MeterInUse(lastRead: S(20), p, S(80) + RaidPresence.Hold + S(1)));   // left the dungeon
    }

    [Fact]
    public void A_read_from_an_earlier_dungeon_does_not_show_the_window_in_the_next()
    {
        var first = RaidPresence.None.Saw(true, true, S(10));
        var next = first.Saw(true, false, S(10) + RaidPresence.Hold + S(30));   // another raid's entry, before its first hit
        Assert.False(WindowLinger.MeterInUse(lastRead: S(10), next, S(10) + RaidPresence.Hold + S(31)));
    }

    [Fact]
    public void Out_of_a_dungeon_the_window_keeps_its_ten_seconds() =>
        Assert.True(WindowLinger.MeterInUse(lastRead: S(10), RaidPresence.None, S(15)));

    [Fact]
    public void The_meter_window_stays_through_a_long_cutscene()
    {
        var p = RaidPresence.None.Saw(true, true, S(10));
        for (int t = 11; t <= 200; t++) p = p.Hidden(S(t));
        p = p.Saw(true, false, S(201));   // the meter back after the cutscene, before the next hit
        Assert.True(WindowLinger.MeterInUse(lastRead: S(10), p, S(201)));
    }

    // The channel box's gold arrow hides the 미터기 at once: no ten seconds after it (channel-cue.plan.md).
    [Fact]
    public void The_gold_arrow_hides_the_meter_without_its_linger()
    {
        var p = RaidPresence.None.Saw(true, true, S(10)).Outside(S(12));
        Assert.False(WindowLinger.MeterInUse(lastRead: S(11), p, S(12)));
        Assert.False(WindowLinger.MeterInUse(lastRead: S(11), p, S(20)));
    }

    // A probe read in the last frames inside, just after the arrow, doesn't bring the 미터기 back for its linger.
    [Fact]
    public void A_read_within_the_block_after_the_arrow_shows_nothing()
    {
        var p = RaidPresence.None.Saw(true, true, S(10)).Outside(S(12));
        Assert.False(WindowLinger.MeterInUse(lastRead: S(12) + RaidPresence.OutBlock, p, S(15)));
    }

    [Fact]
    public void A_read_after_the_arrow_shows_the_meter_again() =>
        Assert.True(WindowLinger.MeterInUse(lastRead: S(30), RaidPresence.None.Outside(S(12)), S(31)));
}