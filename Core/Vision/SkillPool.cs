using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Tells a greyed-out bar's 직업각인 by its 일반 skills (스킬 등록): each 직업각인 has a pool of 일반 slot crops submitted
/// in colour, and a greyed slot counts for the one whose pool holds its skill, wherever it sat on the bar.</summary>
public static class SkillPool
{
    // Measured (SkillPoolTests): a greyed slot scores 0.89–1.00 against its own skill in colour, 0.82 at most against another
    // skill on the same bar; the same skill twice in colour 0.99 and up.
    const double MinScore = 0.85, MinLead = 0.05, SameScore = 0.95;
    // 5 of 8: of the slots whose skill isn't in two pools, the share one pool must hold; never fewer than MinNeeded.
    const double NeededShare = 5.0 / 8;
    const int MinNeeded = 3;

    /// <summary>The 직업각인 in <paramref name="pools"/> holding 5 of 8 of the greyed <paramref name="slots"/>' skills, or null. A
    /// skill in two pools counts for neither and is left out of the 8 (two such: 5 of 8 of the other 6). A 직업각인 with nothing
    /// submitted is never picked; one submitted alone is picked by its own skills.</summary>
    public static string? Pick(IReadOnlyList<Image> slots, IReadOnlyDictionary<string, IReadOnlyList<Image>> pools)
    {
        var counts = pools.Keys.ToDictionary(k => k, _ => 0);
        int shared = 0;
        foreach (var slot in slots)
        {
            var scored = pools.Where(p => p.Value.Count > 0).Select(p => (p.Key, Score: p.Value.Max(i => SubclassMatcher.SkillScore(slot, i))))
                .OrderByDescending(s => s.Score).ToList();
            if (scored.Count == 0 || scored[0].Score < MinScore) continue;
            if (scored.Count > 1 && scored[0].Score - scored[1].Score < MinLead) { shared++; continue; }
            counts[scored[0].Key]++;
        }
        int needed = Math.Max(MinNeeded, (int)Math.Ceiling(NeededShare * (slots.Count - shared)));
        var best = counts.OrderByDescending(c => c.Value).ToList();
        return best.Count > 0 && best[0].Value >= needed && (best.Count == 1 || best[1].Value < best[0].Value) ? best[0].Key : null;
    }

    /// <summary>For each of <paramref name="candidates"/>, whether it is a skill not yet in <paramref name="pool"/> nor earlier
    /// among the candidates, so worth adding.</summary>
    public static bool[] Fresh(IReadOnlyList<Image> pool, IReadOnlyList<Image> candidates)
    {
        var kept = pool.ToList();
        var fresh = new bool[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            fresh[i] = kept.All(k => SubclassMatcher.SkillScore(candidates[i], k) < SameScore);
            if (fresh[i]) kept.Add(candidates[i]);
        }
        return fresh;
    }
}
