using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>A fight's 클리어 / 실패 once the next 전분 record shows its boss (user, 2026-11-03): the same boss again is a retry, so
/// the fight failed; another boss is the next gate, so it was cleared. The boss is its bar's max HP (any of the fight's phases:
/// a retry starts on the first one again), else the gate's name. Pure.</summary>
public static class FightVerdict
{
    /// <summary>The max HP read wobbles in its last digits (1,142,325,546,574 / …571); gates differ by far more.</summary>
    const double SameHp = 0.001;

    /// <summary>Cleared (true), failed (false), or null when neither the next max HP nor the gate names can say.</summary>
    public static bool? ByNext(FightRecord fight, long? nextMax, string? nextGate, IReadOnlyList<RaidGate>? gates = null)
    {
        if (gates is not null && FightRegions.EndedHidden(fight.Boss)) return AfterHidden(fight, nextMax, gates);
        var maxes = fight.Boss.Select(b => b.Max).OfType<long>().Where(m => m > 0).Distinct().ToList();
        if (nextMax is > 0 and var next && maxes.Count > 0) return !maxes.Any(m => Math.Abs(m - next) <= m * SameHp);
        if (!string.IsNullOrEmpty(nextGate) && !string.IsNullOrEmpty(fight.Gate)) return nextGate != fight.Gate;
        return null;
    }

    /// <summary>After a fight ended on ??? (its raid's last phase) no next gate can come: the same raid again is a retry of it, wherever
    /// the retry starts (2-1, whose HP this file doesn't hold); another raid leaves the 클리어; a boss no gate knows says nothing.</summary>
    static bool? AfterHidden(FightRecord fight, long? nextMax, IReadOnlyList<RaidGate> gates)
    {
        if (RaidData.ByHp(gates, nextMax) is not var (next, _)) return null;
        return string.IsNullOrEmpty(fight.Gate) ? null : !RaidData.IsRaidNamed(next, fight.Gate);
    }
}
