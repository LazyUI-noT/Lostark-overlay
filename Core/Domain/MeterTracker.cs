using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>The meter as read on this PC: the 전분 panel's last reading (null: unread), the boss damage of the current record,
/// the record's clock, whether the panel is (still) being read, when it last read, and when the boss HP was last read.</summary>
public sealed record MeterStatus(JeonbunReading? Panel, BossDamageState Boss, FightClockState Clock, bool PanelFound, TimeSpan LastRead = default,
    TimeSpan HpAt = default)
{
    public static readonly MeterStatus None = new(null, BossDamageState.None, FightClockState.None, false);
}

/// <summary>The meter's once-a-second step (spec § C): the 전분 timer moves the record (a new one starts the boss damage over,
/// so it counts from the HP the record first saw), then the boss HP read adds to it. A read missed now and then (a flash behind
/// the see-through panel) keeps the last figures for up to <see cref="Hold"/>, so the meter doesn't flicker; a longer gap
/// (a cutscene, the inventory over it) clears them. Pure.</summary>
public static class MeterTracker
{
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(5);

    public static MeterStatus Step(MeterStatus s, (long Current, long Max)? hp, JeonbunReading? panel, bool panelFound, TimeSpan now, long shield = 0)
    {
        var clock = FightClock.Update(s.Clock, panel?.Timer, now);
        var boss = clock.Fight != s.Clock.Fight ? BossDamageState.None : s.Boss;
        var hpAt = hp is not null ? now : s.HpAt;
        if (panel is not null) return new(panel, BossDamage.Update(boss, hp, shield), clock, true, now, hpAt);
        bool held = s.Panel is not null && now - s.LastRead < Hold;
        return new(held || panelFound ? s.Panel : null, BossDamage.Update(boss, hp, shield), clock, held || panelFound, s.LastRead, hpAt);
    }

    /// <summary>My figures for the party (id, name and party are the session's), or null while the panel isn't read.</summary>
    public static MeterSample? Mine(MeterStatus s, MeterRole role) =>
        s.Panel is not { } p ? null
        : new(0, "", 0, role, 1, p.Damage, p.Dps, p.Timer, !s.Clock.Frozen, s.Clock.Fight, BossDamage.Taken(s.Boss));
}
