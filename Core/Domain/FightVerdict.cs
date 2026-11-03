namespace GaugeOverlay.Core.Domain;

/// <summary>A fight's 클리어 / 실패 once the next 전분 record shows its boss (user, 2026-11-03): the same boss again is a retry, so
/// the fight failed; another boss is the next gate, so it was cleared. The boss is its bar's max HP (any of the fight's phases:
/// a retry starts on the first one again), else the gate's name. Pure.</summary>
public static class FightVerdict
{
    /// <summary>The max HP read wobbles in its last digits (1,142,325,546,574 / …571); gates differ by far more.</summary>
    const double SameHp = 0.001;

    /// <summary>Cleared (true), failed (false), or null when neither the next max HP nor the gate names can say.</summary>
    public static bool? ByNext(FightRecord fight, long? nextMax, string? nextGate)
    {
        var maxes = fight.Boss.Select(b => b.Max).OfType<long>().Where(m => m > 0).Distinct().ToList();
        if (nextMax is > 0 and var next && maxes.Count > 0) return !maxes.Any(m => Math.Abs(m - next) <= m * SameHp);
        if (!string.IsNullOrEmpty(nextGate) && !string.IsNullOrEmpty(fight.Gate)) return nextGate != fight.Gate;
        return null;
    }
}
