using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>전투 기록's 레이드순 (user, 2026-11-11): fights under their raid, raids in the user's order (else the highest entry
/// level first), inside a raid 1관 before 2관, each gate's highest difficulty first, newest first within. The raid comes from the
/// fight's gate (raids.json gates, 숙제 raids). Pure.</summary>
public static class RaidOrder
{
    /// <summary>The raid a fight's gate label belongs to and the gate's place in it; null when no raid lists it.</summary>
    public static (HomeworkRaid Raid, int Gate)? Of(string? gateLabel, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids)
    {
        if (string.IsNullOrEmpty(gateLabel) || gates.FirstOrDefault(g => RaidData.Label(g) == gateLabel) is not { } gate) return null;
        return raids.FirstOrDefault(r => r.Gates.Contains(gate.Id)) is { } raid ? (raid, raid.Gates.ToList().IndexOf(gate.Id)) : null;
    }

    /// <summary>The raids fights can be sorted under (those with gates): <paramref name="userOrder"/>'s ids first in that order,
    /// the rest by their highest entry level, highest first.</summary>
    public static IReadOnlyList<HomeworkRaid> Raids(IReadOnlyList<HomeworkRaid> raids, IReadOnlyList<string> userOrder)
    {
        int Placed(HomeworkRaid r) => userOrder.ToList().IndexOf(r.Id) is >= 0 and var i ? i : int.MaxValue;
        return [.. raids.Where(r => r.Gates.Count > 0).OrderBy(Placed).ThenByDescending(r => r.Levels.Select(l => l.Entry ?? 0).DefaultIfEmpty(0).Max())];
    }

    /// <summary>The order with <paramref name="id"/> moved <paramref name="step"/> places (−1 up, +1 down), every raid written out.</summary>
    public static IReadOnlyList<string> Move(IReadOnlyList<HomeworkRaid> raids, IReadOnlyList<string> userOrder, string id, int step)
    {
        var ids = Raids(raids, userOrder).Select(r => r.Id).ToList();
        int from = ids.IndexOf(id), to = from + step;
        if (from < 0 || to < 0 || to >= ids.Count) return ids;
        (ids[from], ids[to]) = (ids[to], ids[from]);
        return ids;
    }

    /// <summary>A raid's difficulties for the 편집 list, highest first; null when the gate is no known raid's.</summary>
    public static IReadOnlyList<string>? Difficulties(string? gateLabel, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids) =>
        Of(gateLabel, gates, raids) is { } at ? [.. at.Raid.Levels.Select(l => l.Difficulty).Reverse()] : null;

    /// <summary>The fights in groups: each sorted raid that has fights, then the fights of no known raid as
    /// <see cref="Timeline.ByRaid"/> groups them, under a null raid.</summary>
    public static IReadOnlyList<(HomeworkRaid? Raid, IReadOnlyList<T> Fights)> Group<T>(IReadOnlyList<T> newestFirst, Func<T, string?> gate,
        Func<T, string?> difficulty, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids, IReadOnlyList<string> userOrder)
    {
        var sorted = Raids(raids, userOrder);
        var at = newestFirst.Select(x => (Fight: x, At: Of(gate(x), gates, sorted))).ToList();
        var groups = new List<(HomeworkRaid?, IReadOnlyList<T>)>();
        foreach (var raid in sorted)
        {
            int Rank(T x) => raid.Levels.ToList().FindIndex(l => l.Difficulty == difficulty(x));   // −1: unknown, last
            var mine = at.Where(x => x.At?.Raid.Id == raid.Id)   // OrderBy is stable: newest first within
                .OrderBy(x => x.At!.Value.Gate).ThenByDescending(x => Rank(x.Fight)).Select(x => x.Fight).ToList();   // user, 2026-11-11: gate first
            if (mine.Count > 0) groups.Add((raid, mine));
        }
        var rest = at.Where(x => x.At is null).Select(x => x.Fight).ToList();
        if (rest.Count > 0) groups.Add((null, Timeline.ByRaid(rest, gate, difficulty)));
        return groups;
    }
}
