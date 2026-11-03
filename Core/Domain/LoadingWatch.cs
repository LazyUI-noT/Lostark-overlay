namespace GaugeOverlay.Core.Domain;

/// <summary>What a loading screen means for where the player is (meter-gating.plan.md phase 2, user 2026-11-04). The bar is
/// read only while the HUD is hidden; <see cref="Needed"/> reads in a row make a loading. When the HUD is back, a stay the
/// in-game meter showed ends if neither the meter nor a boss bar is seen within <see cref="MeterWait"/>: the raid is over, no
/// 60 s hold. A stay shown by a boss bar alone (the meter hidden) keeps the hold: no meter proves nothing there. The wait
/// counts only steps with the HUD up and the game in front (<see cref="Tick"/>): an intro cutscene or an alt-tab pauses it.
/// <see cref="WaitFrom"/> is the wait's last step (null: no wait), <see cref="Waited"/> the time counted so far. Pure.</summary>
public sealed record LoadingWatch(int Run, bool Loading, bool LeaveIfNoMeter, TimeSpan? WaitFrom, TimeSpan Waited = default)
{
    public const int Needed = 2;
    public static readonly TimeSpan MeterWait = TimeSpan.FromSeconds(5);
    public static readonly LoadingWatch None = new(0, false, false, null);

    /// <summary>A bar read with the HUD hidden. The read that confirms a loading notes whether the meter showed this stay.</summary>
    public LoadingWatch Read(bool bar, RaidPresence p, TimeSpan t)
    {
        if (!bar) return this with { Run = 0 };
        int run = Run + 1;
        if (Loading || run < Needed) return this with { Run = run };
        bool byMeter = p.InDungeon(t) && p.MeterSeen is { } m && p.Since is { } since && m >= since;
        return new(run, true, byMeter, null);
    }

    /// <summary>The HUD is back: after a loading, the wait for the meter starts.</summary>
    public LoadingWatch HudBack(TimeSpan t) => Loading ? new(0, false, LeaveIfNoMeter, t) : this with { Run = 0 };

    /// <summary>A step: the wait counts the time since the last one only when <paramref name="counting"/> (HUD up, game in front).</summary>
    public LoadingWatch Tick(bool counting, TimeSpan t) =>
        WaitFrom is not { } last ? this : this with { WaitFrom = t, Waited = Waited + (counting && t > last ? t - last : TimeSpan.Zero) };

    /// <summary>The meter or a boss bar seen: the stay goes on (a gate change) or began (an entry).</summary>
    public LoadingWatch Saw() => WaitFrom is null ? this : None;

    /// <summary>The player left: a loading out of a stay the meter showed, and nothing seen for <see cref="MeterWait"/> counted after.</summary>
    public bool Left => LeaveIfNoMeter && WaitFrom is not null && Waited >= MeterWait;
}
