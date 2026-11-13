using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>A level band: a fight whose entry item level is <paramref name="UpTo"/> or less is capped at <paramref name="Cap"/> %.</summary>
public sealed record BuffBand(int UpTo, int Cap);

/// <summary>The buff lock's caps (Profiles/raids.json "buffCaps"; user, 2026-11-13): the first band a fight's entry level fits, else
/// <paramref name="Fallback"/> % (also for a fight whose raid or difficulty isn't known). A gate's own "buffCap" goes over the bands.</summary>
public sealed record BuffCapRules(int Fallback, IReadOnlyList<BuffBand> Bands)
{
    public static readonly BuffCapRules Default = new(85, []);
}

/// <summary>What the buff lock leaves of 버프 적용률 on this PC's screen (buff-lock.design.md; user, 2026-11-12): 공증 only, shown
/// as "85+%" from 85% up; the cap is per fight since 2026-11-13 (<see cref="CapFor"/>: 80, 85, 90 or no lock). The live buff bars, saved fights and what a party member sends stay complete.</summary>
public static class BuffLock
{
    /// <summary>The cap (0 → 1) on a locked PC for a fight in <paramref name="gate"/> at <paramref name="difficulty"/>: the gate's own
    /// cap for it, else the band of its entry level (싱글 and 매칭 use 노말's), else the rules' fallback; null: no lock in it.</summary>
    public static double? CapFor(RaidGate? gate, string? difficulty, BuffCapRules rules)
    {
        if (gate is null || difficulty is null) return rules.Fallback / 100.0;
        if (gate.BuffCaps.TryGetValue(difficulty, out var own) && own is null or (> 0 and <= 100)) return own / 100.0;   // else: a bad value, the bands
        string levelKey = difficulty is "싱글" or "매칭" ? "노말" : difficulty;
        if (!gate.Levels.TryGetValue(levelKey, out var level)) return rules.Fallback / 100.0;
        return (rules.Bands.OrderBy(b => b.UpTo).FirstOrDefault(b => level <= b.UpTo)?.Cap ?? rules.Fallback) / 100.0;
    }

    /// <summary>A locked fight's 아덴 피증 line shows this long after the fight's end, so it can't be checked between tries.</summary>
    public static readonly TimeSpan AdenAfter = TimeSpan.FromMinutes(10);

    /// <summary>The cap for a fight: its gate and difficulty by the boss's exact max HP, else <paramref name="named"/> (recognised by
    /// name) at the dungeon panel's <paramref name="difficulty"/>.</summary>
    public static double? CapFor(IReadOnlyList<RaidGate> gates, BuffCapRules rules, long? bossMax, RaidGate? named, string? difficulty) =>
        RaidData.ByHp(gates, bossMax) is { } byHp ? CapFor(byHp.Gate, byHp.Difficulty, rules) : CapFor(named, difficulty, rules);

    /// <summary>A share as the label shows it: "85+%" once it would read the <paramref name="cap"/> (here 85%) or more; null: unlocked.</summary>
    public static string Figure(double share, double? cap)
    {
        double percent = Math.Round(Math.Clamp(share, 0, 1) * 100, MidpointRounding.AwayFromZero);   // as "{x:0}" rounds
        return cap is { } c && percent >= Math.Round(c * 100) ? $"{c * 100:0}+%" : $"{percent:0}%";
    }

    /// <summary>A bar's filled part, 0 → 1; locked it stops at the <paramref name="cap"/>.</summary>
    public static double Bar(double share, double? cap) => Math.Min(Math.Clamp(share, 0, 1), cap ?? 1);

    /// <summary>The meter's buff rows cut to 공증's (the first, by <see cref="BuffKind"/>) and marked with the cap; null: unlocked.</summary>
    public static MeterView Apply(MeterView v, double? cap) => cap is null ? v : v with
    {
        BuffCap = cap,
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
