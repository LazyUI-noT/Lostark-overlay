namespace GaugeOverlay.Core.Domain;

/// <summary>The 전분 record as its timer shows it: the last value read and when, when it last moved, which record (fight) it
/// is (0: none seen yet), whether it is stopped (a phase change, the end of a fight), and a lower read waiting for a second to
/// confirm a new record (<see cref="Lower"/>).</summary>
public sealed record FightClockState(int? Timer, TimeSpan SeenAt, TimeSpan MovedAt, int Fight, bool Frozen, int? Lower = null)
{
    public static readonly FightClockState None = new(null, TimeSpan.Zero, TimeSpan.Zero, 0, false);
}

/// <summary>The 전분 record's lifecycle (spec § C Lifecycle): its timer read once a second counts up while the fight runs, stays
/// put while it is stopped (frozen after <see cref="FrozenAfter"/>), and goes back when a new record starts (two reads agreeing). Unreadable reads
/// change nothing. Pure.</summary>
public static class FightClock
{
    public static readonly TimeSpan FrozenAfter = TimeSpan.FromSeconds(1.5);

    public static FightClockState Update(FightClockState s, int? timerSeconds, TimeSpan now)
    {
        if (timerSeconds is not { } t || t < 0) return s;
        if (s.Timer is not { } was) return new(t, now, now, s.Fight + 1, false);
        if (t < was)   // a new record once the next read agrees (counting on from the lower value), so one misread is not
            return s.Lower is { } l && t >= l && t <= l + 2 ? new(t, now, now, s.Fight + 1, false) : s with { Lower = t, SeenAt = now };
        if (t > was) return new(t, now, now, s.Fight, false);
        return s with { SeenAt = now, Lower = null, Frozen = now - s.MovedAt >= FrozenAfter };
    }

    /// <summary>A new record after another one: the next gate (no loading screen between gates, its record starts at 00:00)
    /// or a retry. The first record seen is not one.</summary>
    public static bool Restarted(FightClockState was, FightClockState now) => was.Fight > 0 && now.Fight != was.Fight;

    /// <summary>A clock read this recently decides <see cref="Running"/>; an older one (the 전분 closed, the meter off) doesn't.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(3);

    /// <summary>A fight is on: the 전분 timer read lately and not stopped, or without such a read a boss bar up. Used to hold the
    /// game menu's OCR (perf-hotspots.plan.md step 2): the menu is opened in a dungeon once the 전분 stopped (user, 2026-11-08).</summary>
    public static bool Running(FightClockState clock, TimeSpan now, bool bossUp) =>
        clock.Timer is not null && now - clock.SeenAt <= FreshFor ? !clock.Frozen : bossUp;
}
