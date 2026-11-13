namespace GaugeOverlay.Core.Domain;

/// <summary>What the buff lock leaves of 버프 적용률 on this PC's screen (buff-lock.design.md; user, 2026-11-12): 공증 only, shown
/// as "85+%" from 85% up. The live buff bars, saved fights and what a party member sends stay complete.</summary>
public static class BuffLock
{
    public const double Cap = 0.85;

    /// <summary>A locked fight's 아덴 피증 line shows this long after the fight's end, so it can't be checked between tries.</summary>
    public static readonly TimeSpan AdenAfter = TimeSpan.FromMinutes(10);

    /// <summary>A share as the label shows it: "85+%" once it would read 85% or more while locked.</summary>
    public static string Figure(double share, bool locked)
    {
        double percent = Math.Round(Math.Clamp(share, 0, 1) * 100, MidpointRounding.AwayFromZero);   // as "{x:0}" rounds
        return locked && percent >= Cap * 100 ? $"{Cap * 100:0}+%" : $"{percent:0}%";
    }

    /// <summary>A bar's filled part, 0 → 1; locked it stops at <see cref="Cap"/>.</summary>
    public static double Bar(double share, bool locked) => Math.Min(Math.Clamp(share, 0, 1), locked ? Cap : 1);

    /// <summary>The meter's buff rows cut to 공증's (the first, by <see cref="BuffKind"/>) and marked locked.</summary>
    public static MeterView Apply(MeterView v, bool locked) => !locked ? v : v with
    {
        BuffLocked = true,
        BuffCover = v.BuffCover?.Take(1).ToList(),
        BuffParty = v.BuffParty?.Take(1).ToList(),
        BuffSplit = v.BuffSplit?.Take(1).ToList(),
    };

    /// <summary>The timeline's buff lanes: locked, 초각스 피증 has none.</summary>
    public static IReadOnlyList<BuffKind> TimelineKinds(bool locked) =>
        locked ? [BuffKind.공증, BuffKind.아덴피증] : [BuffKind.공증, BuffKind.아덴피증, BuffKind.초각스피증];

    /// <summary>Whether a buff's line (and its hover tag) is drawn for <paramref name="r"/> at <paramref name="now"/> (local, as
    /// <see cref="FightRecord.StartedAt"/>).</summary>
    public static bool LineShown(BuffKind kind, bool locked, FightRecord r, DateTime now) => !locked || kind switch
    {
        BuffKind.공증 => true,
        BuffKind.아덴피증 => now >= r.StartedAt.AddSeconds(r.Duration) + AdenAfter,
        _ => false,
    };

    /// <summary>Locked, only 공증 keeps a % (capped by <see cref="Figure"/>).</summary>
    public static bool PercentShown(BuffKind kind, bool locked) => !locked || kind == BuffKind.공증;
}
