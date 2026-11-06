using System.Collections.Immutable;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>A slot's identification so far: the skill committed (two agreeing reads), the name the last read gave and how many
/// in a row, the best candidates while unknown (for 편집), and the reads tried and when last.</summary>
public sealed record SlotGuess(LibrarySkill? Skill, string? Pending, int Agree, IReadOnlyList<LibrarySkill> Candidates, int Tries, TimeSpan? Last);

/// <summary>Which skill is on each slot during a fight (skill-icons.plan.md), from ready crops of the skill bar: each slot only
/// against its kind's pool in <see cref="Class"/>. For a fight of unknown class (테스트 모드) the first crops vote a class
/// first (<see cref="SkillMatch.ClassVote"/>). A new state per crop; <see cref="Apply"/> writes the result into the record. Pure.</summary>
public sealed record FightSkills(string? Class, ImmutableDictionary<SlotId, SlotGuess> Slots,
    ImmutableDictionary<SlotId, IReadOnlyList<IconPrint>> VoteCrops, bool Voted, ImmutableDictionary<SlotId, string>? SlotVotes = null)
{
    /// <summary>An unknown slot asks for a crop this often, <see cref="MaxTries"/> times; a known one is checked again every
    /// <see cref="RecheckInterval"/> (a skill swapped in town before the pull).</summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(1), RecheckInterval = TimeSpan.FromSeconds(10);
    // ponytail: 30 reads bound the work on a slot that never matches (a skill not in the library); raise if replays show late ones.
    public const int MaxTries = 30;

    static readonly SlotGuess Fresh = new(null, null, 0, [], 0, null);

    /// <summary>A new fight's state; <paramref name="cls"/> null when the class isn't known ([Test]).</summary>
    public static FightSkills For(string? cls) => new(cls, ImmutableDictionary<SlotId, SlotGuess>.Empty, ImmutableDictionary<SlotId, IReadOnlyList<IconPrint>>.Empty, false);

    /// <summary>The pool a slot is matched in: 일반 1–8 the 일반 skills, 초각스, 각성기 (각성기 and 초각성기); 아덴 none.</summary>
    public static SlotGroup? KindOf(SlotId slot) => slot switch
    {
        >= SlotId.일반1 and <= SlotId.일반8 => SlotGroup.일반스킬,
        SlotId.초각스 => SlotGroup.초각스,
        SlotId.각성기 => SlotGroup.각성기,
        _ => null,
    };

    public LibrarySkill? Skill(SlotId slot) => Slots.GetValueOrDefault(slot)?.Skill;

    /// <summary>The map for the class played now (<paramref name="cls"/>; null in 테스트 모드): this one, or a new one when the class
    /// changed. A [Test] fight keeps the class it voted; a real character's fight never carries a vote.</summary>
    public FightSkills Current(string? cls) =>
        cls is not null ? (Class == cls && !Voted ? this : For(cls)) : Class is null || Voted ? this : For(null);

    /// <summary>A match of <paramref name="slot"/> that threw: counted as a try, so the slot waits like any other.</summary>
    public FightSkills Failed(SlotId slot, TimeSpan now) =>
        KindOf(slot) is null ? this : this with { Slots = Slots.SetItem(slot, (Slots.GetValueOrDefault(slot) ?? Fresh) is var g ? g with { Tries = g.Tries + 1, Last = now } : Fresh) };

    /// <summary>Whether the engine should send this slot's next ready crop.</summary>
    public bool Wanted(SlotId slot, TimeSpan now)
    {
        if (KindOf(slot) is null) return false;
        if (Slots.GetValueOrDefault(slot) is not { Last: { } last } g) return true;
        return g.Skill is not null ? now - last >= RecheckInterval : g.Tries < MaxTries && now - last >= WatchInterval;
    }

    /// <summary>One ready crop of <paramref name="slot"/> (<see cref="IconPrint.Framings"/>).</summary>
    public FightSkills Add(SlotId slot, IReadOnlyList<IconPrint> framings, SkillLibrary lib, TimeSpan now)
    {
        if (KindOf(slot) is null) return this;
        if (Class is not null) return Identify(slot, framings, lib, now);
        // Each slot votes once (a vote scores every class): one that voted keeps its vote, one that didn't tries its new crop.
        var votes = SlotVotes ?? ImmutableDictionary<SlotId, string>.Empty;
        if (!votes.ContainsKey(slot) && SkillMatch.VoteOf(KindOf(slot)!.Value, framings, lib) is { } vote) votes = votes.SetItem(slot, vote);
        var crops = VoteCrops.SetItem(slot, framings);
        var g = Slots.GetValueOrDefault(slot) ?? Fresh;
        var waiting = this with { VoteCrops = crops, SlotVotes = votes, Slots = Slots.SetItem(slot, g with { Tries = g.Tries + 1, Last = now }) };
        if (SkillMatch.Winner(votes.Values.GroupBy(c => c).ToDictionary(c => c.Key, c => c.Count())) is not { } cls) return waiting;
        // The winner: every crop the vote saw is read again in that class (the newest one last).
        var known = waiting with { Class = cls, Voted = true, Slots = ImmutableDictionary<SlotId, SlotGuess>.Empty, VoteCrops = VoteCrops.Clear(), SlotVotes = null };
        foreach (var (s, f) in crops.Where(c => c.Key != slot)) known = known.Identify(s, f, lib, now);
        return known.Identify(slot, framings, lib, now);
    }

    FightSkills Identify(SlotId slot, IReadOnlyList<IconPrint> framings, SkillLibrary lib, TimeSpan now)
    {
        var g = Slots.GetValueOrDefault(slot) ?? Fresh;
        var r = SkillMatch.Identify(framings, lib.Pool(Class!, KindOf(slot)!.Value));
        g = r.Skill is not { } found
            ? g with { Pending = null, Agree = 0, Candidates = g.Skill is null && r.Top.Count > 0 ? r.Top : g.Candidates }
            : (found.Name == g.Pending ? g.Agree + 1 : 1) is var agree && agree >= 2
                ? g with { Skill = found, Pending = found.Name, Agree = agree, Candidates = [] }
                : g with { Pending = found.Name, Agree = agree, Candidates = g.Skill is null ? r.Top : g.Candidates };
        return this with { Slots = Slots.SetItem(slot, g with { Tries = g.Tries + 1, Last = now }) };
    }

    /// <summary>편집's 적용 (plan Phase 4): <paramref name="edited"/> is the fight as edited, <paramref name="original"/> as it was.
    /// A [Test] fight given its proposed class keeps the skills found in it and the proposal is done; another class drops them (the
    /// slots can't be matched again without their crops); a class left as it was keeps the proposal. Each pick from an unsure
    /// slot's candidates makes it known.</summary>
    public static FightRecord Confirm(FightRecord original, FightRecord edited, IReadOnlyDictionary<SlotId, RecordSkill> picks)
    {
        var skills = new Dictionary<SlotId, RecordSkill>(original.Skills ?? new Dictionary<SlotId, RecordSkill>());
        var unsure = new Dictionary<SlotId, IReadOnlyList<RecordSkill>>(original.Unsure ?? new Dictionary<SlotId, IReadOnlyList<RecordSkill>>());
        foreach (var (slot, pick) in picks)
            if (unsure.Remove(slot)) skills[slot] = pick;
        string? proposed = original.ProposedClass;
        if (proposed is not null && original.MyClassName == RecordCharacter.TestClass && edited.MyClassName is { } cls && cls != RecordCharacter.TestClass)
        {
            if (cls != proposed.Split(" · ")[0]) { skills.Clear(); unsure.Clear(); }
            proposed = null;
        }
        return edited with { Skills = skills.Count > 0 ? skills : null, Unsure = unsure.Count > 0 ? unsure : null, ProposedClass = proposed };
    }

    /// <summary>The record with what was found: the identified slots, the candidates of the unsure ones and, when the class was
    /// voted, the proposal ("클래스 · 직업각인", the 직업각인 from the 초각스 found; the class alone without).</summary>
    public FightRecord Apply(FightRecord r)
    {
        static RecordSkill Of(LibrarySkill s) => new(s.Name, s.Icon, s.Kind);
        var skills = Slots.Where(s => s.Value.Skill is not null).ToDictionary(s => s.Key, s => Of(s.Value.Skill!));
        var unsure = Slots.Where(s => s.Value.Skill is null && s.Value.Candidates.Count > 0)
            .ToDictionary(s => s.Key, s => (IReadOnlyList<RecordSkill>)[.. s.Value.Candidates.Take(3).Select(Of)]);
        string? proposed = Voted && Class is not null ? RecordCharacter.Label(Class, Skill(SlotId.초각스)?.Subclass) : null;
        return r with { Skills = skills.Count > 0 ? skills : null, Unsure = unsure.Count > 0 ? unsure : null, ProposedClass = proposed };
    }
}
