using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Which skill a skill-bar capture shows (skill-icons.plan.md): per category, a slot only against its own kind's pool
/// (일반 against 일반스킬, 초각스 against 초각스, 각성기 against 각성기 + 초각성기), and for a fight of unknown class a vote of
/// the slots over all classes. Unknown rather than a guess. Pure.</summary>
public static class SkillMatch
{
    /// <summary>The skill identified (null: unknown), the three best candidates and the best score.</summary>
    public sealed record Result(LibrarySkill? Skill, IReadOnlyList<LibrarySkill> Top, double Score);

    // IconPrintSweep (GAUGE_ICON_VOTE), 2026-11-05: 7 saved sets (워로드, 발키리, 도화가 ×2, 바드 from compressed video) against
    // the 30 shipped classes: every vote for the right class (바드 4, the others 8–10), none for a wrong one. A wrong class was
    // top only on two unknown 바드 slots, at 0.657 at most; another class scored up to 0.775 where the right one led by 0.149.
    // ponytail: 0.75 keeps 바드's video (0.8 would propose nothing there); recheck on the footage replays (plan Phase 5).
    public const double ClassMinScore = 0.75, ClassMinGap = 0.05;
    public const int MinVotes = 4, VoteLead = 2;

    /// <summary>The best of every framing against every print; 0 without either (a crop too small, a skill without prints).</summary>
    static double Score(IReadOnlyList<IconPrint> framings, LibrarySkill skill)
    {
        double best = 0;
        foreach (var p in skill.Prints)
            foreach (var f in framings) best = Math.Max(best, f.Similarity(p));
        return best;
    }

    /// <summary>Best in <paramref name="pool"/> (one class's skills of one kind), by <see cref="IconPrint.MinScore"/> and
    /// <see cref="IconPrint.MinGap"/> over the next other skill.</summary>
    public static Result Identify(IReadOnlyList<IconPrint> framings, IReadOnlyList<LibrarySkill> pool)
    {
        if (framings.Count == 0) return new(null, [], 0);
        var ranked = pool.Select(s => (Skill: s, Score: Score(framings, s))).OrderByDescending(t => t.Score).ToList();
        if (ranked.Count == 0) return new(null, [], 0);
        double next = ranked.Skip(1).FirstOrDefault(t => t.Skill.Name != ranked[0].Skill.Name).Score;
        bool sure = ranked[0].Score >= IconPrint.MinScore && ranked[0].Score - next >= IconPrint.MinGap;
        return new(sure ? ranked[0].Skill : null, [.. ranked.Take(3).Select(t => t.Skill)], ranked[0].Score);
    }

    /// <summary>Votes per class: a slot votes for the class whose same-kind pool holds its best match, when that match scores
    /// <see cref="ClassMinScore"/>, leads every other class by <see cref="ClassMinGap"/> and isn't an icon two classes share.</summary>
    public static IReadOnlyDictionary<string, int> Votes(IEnumerable<(SlotGroup Kind, IReadOnlyList<IconPrint> Framings)> slots, SkillLibrary lib) =>
        slots.Select(s => VoteOf(s.Kind, s.Framings, lib)).OfType<string>().GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>The class one slot votes for (see <see cref="Votes"/>); null: none.</summary>
    public static string? VoteOf(SlotGroup kind, IReadOnlyList<IconPrint> framings, SkillLibrary lib)
    {
        if (framings.Count == 0) return null;
        var best = lib.Classes.Select(c => BestIn(c, lib.Pool(c, kind), framings)).OrderByDescending(t => t.Score).Take(2).ToList();
        if (best.Count == 0 || best[0].Score < ClassMinScore || lib.Shared(best[0].Icon)) return null;
        return best.Count > 1 && best[0].Score - best[1].Score < ClassMinGap ? null : best[0].Class;
    }

    /// <summary>The skills a class's full search looks at: the best by the capture's first framing alone (the whole crop). The
    /// vote scores every class, ~1,700 skills; this keeps it to a few full searches per class (replays: 614 ms/s → see sweep).</summary>
    const int Shortlist = 5;

    /// <summary>A class's best skill for a capture (an empty pool: no icon, score 0).</summary>
    static (string Class, string Icon, double Score) BestIn(string cls, IReadOnlyList<LibrarySkill> pool, IReadOnlyList<IconPrint> framings)
    {
        (string Class, string Icon, double Score) best = (cls, "", 0);
        IReadOnlyList<IconPrint> first = framings.Count > 0 ? [framings[0]] : [];
        foreach (var s in pool.OrderByDescending(s => Score(first, s)).Take(Shortlist))
            if (Score(framings, s) is var score && score > best.Score) best = (cls, s.Icon, score);
        return best;
    }

    /// <summary>The class with <see cref="MinVotes"/> votes and <see cref="VoteLead"/> more than any other; null otherwise.</summary>
    public static string? ClassVote(IEnumerable<(SlotGroup Kind, IReadOnlyList<IconPrint> Framings)> slots, SkillLibrary lib) => Winner(Votes(slots, lib));

    /// <summary><see cref="ClassVote"/>'s rule over votes already counted.</summary>
    public static string? Winner(IReadOnlyDictionary<string, int> votes)
    {
        var ranked = votes.OrderByDescending(v => v.Value).ToList();
        if (ranked.Count == 0 || ranked[0].Value < MinVotes) return null;
        int runnerUp = ranked.Count > 1 ? ranked[1].Value : 0;
        return ranked[0].Value - runnerUp >= VoteLead ? ranked[0].Key : null;
    }
}
