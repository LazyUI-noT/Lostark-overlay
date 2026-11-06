using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Which class a ready 초각스 shows, against every class's 초각스 in the shipped library (class-detect.plan.md). Comparing
/// with the last class first keeps the usual case to one class's icons. Unknown rather than a guess: a caller keeps the class
/// it has. Pure.</summary>
public static class SkillClassFinder
{
    /// <summary>What a 초각스 showed. <see cref="Class"/> null: can't tell. <see cref="Subclass"/> only where the library knows it.
    /// <see cref="Score"/> and <see cref="Lead"/> (over the best other class) are for the sweep and the log.</summary>
    public sealed record Found(string? Class, string? Subclass, bool Fast, double Score, double Lead);

    /// <summary>What a bar's 일반 slots say of a class. They only confirm: a 카오스 던전 setup may hold other 일반 skills.</summary>
    public enum Verdict { Confirmed, Contradicted, Unsure }

    // IconPrintSweep (GAUGE_CLASS_FIND), 2026-11-05: 7 saved sets (워로드, 발키리, 도화가, 바드 from compressed video) and the greyed
    // fixture against the 30 shipped classes. Colour: right 0.951-0.984 leading by 0.335-0.575, 바드's video 0.712 leading by
    // 0.144; the nearest other class's art 0.598; another slot read as a 초각스 at most 0.713 leading by 0.099. Greyed (brightness
    // only, simulated from those crops, plus the real greyed_초각스 at 0.987 leading by 0.360): right 0.968-0.996 leading by
    // 0.287-0.370, 바드 0.785 (unknown: waits for colour); the nearest other art 0.794; another slot at most 0.872, 0.863 leading
    // by 0.163. Zero wrong picks at these gates; only one real greyed bar has been seen, so grey stays this strict.
    // A 초각스 on cooldown (lt2_11, its digits over the art) reached 0.732 leading by 0.317 in another class: the engine only sends ready
    // ones and the 일반 slots would refuse it, but 0.75 clears it too. Only heavily compressed video falls under it, and that keeps its class.
    public const double MinScore = 0.75, MinLead = 0.12, GreyMinScore = 0.9, GreyMinLead = 0.2;
    public const int ConfirmVotes = 2;

    /// <summary>The class of a ready 초각스 capture. <paramref name="last"/> is tried first and is enough when sure in its own
    /// icons and no other class scores near it; else every class. <paramref name="greyed"/>: the bar is greyed out (a town), so
    /// only brightness counts and the bar's colour says nothing.</summary>
    public static Found Find(IReadOnlyList<IconPrint> framings, SkillLibrary lib, string? last = null, bool greyed = false)
    {
        if (framings.Count == 0) return new(null, null, false, 0, 0);
        double minScore = greyed ? GreyMinScore : MinScore, minLead = greyed ? GreyMinLead : MinLead;
        if (last is not null && BestIn(framings, lib.Pool(last, SlotGroup.초각스), greyed) is { Skill: not null } mine && mine.Score >= FastScore(minScore))
            return new(last, mine.Skill.Subclass, true, mine.Score, 0);
        var ranked = lib.Classes.Select(c => (Class: c, Best: BestIn(framings, lib.Pool(c, SlotGroup.초각스), greyed))).OrderByDescending(t => t.Best.Score).Take(2).ToList();
        if (ranked.Count == 0 || ranked[0].Best.Skill is not { } skill) return new(null, null, false, 0, 0);
        double lead = ranked[0].Best.Score - (ranked.Count > 1 ? ranked[1].Best.Score : 0);
        bool sure = ranked[0].Best.Score >= minScore && lead >= minLead && !lib.Shared(skill.Icon);
        return new(sure ? ranked[0].Class : null, sure ? skill.Subclass : null, false, ranked[0].Best.Score, lead);
    }

    /// <summary>The last class needs less than a search over every class: a little above a one-class match, as the search's lead
    /// isn't asked. A search still follows when it isn't reached.</summary>
    static double FastScore(double minScore) => minScore + 0.05;

    static (LibrarySkill? Skill, double Score) BestIn(IReadOnlyList<IconPrint> framings, IReadOnlyList<LibrarySkill> pool, bool greyed)
    {
        (LibrarySkill? Skill, double Score) best = (null, 0);
        foreach (var skill in pool)
            foreach (var print in skill.Prints)
                foreach (var f in framings)
                    if ((greyed ? f.BrightnessSimilarity(print) : f.Similarity(print)) is var s && s > best.Score) best = (skill, s);
        return best;
    }

    /// <summary>The 일반 slots' say on <paramref name="cls"/>: <see cref="ConfirmVotes"/> votes for it and none ahead confirm;
    /// another class winning the vote (<see cref="SkillMatch.Winner"/>) contradicts; anything else can't tell.</summary>
    public static Verdict Confirm(IEnumerable<IReadOnlyList<IconPrint>> ordinary, SkillLibrary lib, string cls)
    {
        var votes = SkillMatch.Votes(ordinary.Select(f => (SlotGroup.일반스킬, f)), lib);
        int mine = votes.GetValueOrDefault(cls);
        if (SkillMatch.Winner(votes) is { } winner && winner != cls) return Verdict.Contradicted;
        return mine >= ConfirmVotes && votes.All(v => v.Key == cls || v.Value < mine) ? Verdict.Confirmed : Verdict.Unsure;
    }
}
