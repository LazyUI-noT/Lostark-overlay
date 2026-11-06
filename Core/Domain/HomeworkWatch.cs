using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>What the fallback decided: a raid cleared, cleared perhaps (확인필요), or a clear taken back (the same boss came again).
/// <paramref name="Difficulty"/> null: the data can't say which (the service uses the character's pick).</summary>
public enum WatchOutcome { Cleared, Unsure, Failed }

public sealed record WatchDecision(string Raid, string? Difficulty, WatchOutcome Outcome);

/// <summary>The boss bar's text this step: HP now and max HP.</summary>
public sealed record WatchRead(long Current, long Max);

/// <summary><paramref name="Blocked"/>: the game shows nothing to judge by (HUD hidden, another app in front, 대난투); nothing is
/// decided and the bar-gone clock waits. <paramref name="NameGate"/>: the gate 자동 인식 recognises by name.</summary>
public sealed record WatchContext(bool Blocked, RaidGate? NameGate, TimeSpan Now);

/// <summary>A boss bar being followed: its gate (by max HP, else by name once the bar has run a while) and difficulty (by max HP only).</summary>
public sealed record WatchFight(RaidGate? Gate, string? Difficulty, long Max, long Current, TimeSpan Since, TimeSpan Seen, int HiddenReads = 0);

/// <summary>A bar that is gone: when, and what was decided about it then (kept to take it back if the same boss comes again).</summary>
public sealed record WatchEnded(WatchFight Fight, TimeSpan At, WatchDecision? Decided);

/// <summary>숙제's fallback (pyosi-map.plan.md): with the meter or 전투 기록 off there is no recorded fight, so a raid's clear is worked
/// out from the boss bar alone, by the thresholds and the next-boss rule of <see cref="FightRecorder"/> and <see cref="FightVerdict"/>.
/// Raid gates of raids.json only. Pure; stepped about once a second.</summary>
public sealed record HomeworkWatch(WatchFight? Now, WatchEnded? Last)
{
    public static readonly HomeworkWatch None = new(null, null);

    /// <summary>The same boss after a decision, this soon, is a retry: the wipe took the clear back.</summary>
    public static readonly TimeSpan RetryWithin = TimeSpan.FromMinutes(10);
    /// <summary>A name read in the first seconds of a bar may still be the last boss's.</summary>
    public static readonly TimeSpan NameSettle = TimeSpan.FromSeconds(5);
    /// <summary>Reads of ??? in a row that make it the raid's last phase (as <see cref="FightRegions.EndedHidden"/>).</summary>
    const int HiddenReadsNeeded = 3;
    /// <summary>The last HP at or over this share of the max is no end: a wipe, a phase, the party leaving.</summary>
    const double MidShare = 0.10;

    public (HomeworkWatch State, WatchDecision? Decision) Step(WatchRead? read, WatchContext ctx, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids)
    {
        if (ctx.Blocked) return (this with { Now = Now is { } held ? held with { Seen = ctx.Now } : null }, null);
        if (read is null)
            return Now is { } f && ctx.Now - f.Seen >= TimeSpan.FromSeconds(FightRecorder.BarGoneFor) ? BarGone(f, ctx, raids) : (this, null);
        return FightRegions.IsHidden(read.Max) ? HiddenBar(ctx, raids) : Bar(read, ctx, gates, raids);
    }

    /// <summary>One read of a tiny max is a misread ("1/2" at 1 HP), not the ??? phase.</summary>
    static bool WentHidden(WatchFight f) => f.HiddenReads >= HiddenReadsNeeded;

    (HomeworkWatch, WatchDecision?) BarGone(WatchFight f, WatchContext ctx, IReadOnlyList<HomeworkRaid> raids)
    {
        var decision = WentHidden(f) ? null : Finishing(f, OutcomeAt(f, null), raids);   // a raid that went on to ??? was judged there
        return (new HomeworkWatch(null, new WatchEnded(f, ctx.Now, decision)), decision);
    }

    (HomeworkWatch, WatchDecision?) HiddenBar(WatchContext ctx, IReadOnlyList<HomeworkRaid> raids)
    {
        if (Now is not { } f) return (this, null);
        var next = f with { HiddenReads = f.HiddenReads + 1, Seen = ctx.Now };
        var state = this with { Now = next };
        return (state, next.HiddenReads == HiddenReadsNeeded ? HiddenEnd(next, raids) : null);   // ??? says the last phase was reached, not that it was won
    }

    (HomeworkWatch, WatchDecision?) Bar(WatchRead read, WatchContext ctx, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids)
    {
        if (Now is { } f && !FightVerdict.ByMaxes([f.Max], read.Max))
            return (this with { Now = Follow(f, read, ctx, gates) }, null);
        // The same bar back still low is the fight going on (at 1 HP it can drop out for seconds); a retry starts near full.
        if (Now is null && Last is { } back && !FightVerdict.ByMaxes([back.Fight.Max], read.Max) && read.Current < read.Max * MidShare)
            return (new HomeworkWatch(Follow(back.Fight, read, ctx, gates), null), null);
        var ended = Now is { } n ? new WatchEnded(n, n.Seen, null) : Last;   // a bar that never went away, or one that did
        var decision = ended is null ? null : Judge(ended, read.Max, gates, raids, ctx.Now);
        var start = new WatchFight(null, null, read.Max, read.Current, ctx.Now, ctx.Now);
        return (new HomeworkWatch(Follow(start, read, ctx, gates), null), decision);
    }

    static WatchFight Follow(WatchFight f, WatchRead read, WatchContext ctx, IReadOnlyList<RaidGate> gates)
    {
        var (gate, difficulty) = RaidData.ByHp(gates, read.Max) is var (g, d) ? (g, d) : (f.Gate, f.Difficulty);
        if (gate is null && ctx.NameGate is { } named && ctx.Now - f.Since >= NameSettle) gate = named;
        return f with { Gate = gate, Difficulty = difficulty, Current = read.Current, Seen = ctx.Now, HiddenReads = 0 };
    }

    /// <summary>The next bar is another boss than <paramref name="ended"/>'s, or the same again.</summary>
    static WatchDecision? Judge(WatchEnded ended, long nextMax, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids, TimeSpan now)
    {
        if (!FightVerdict.ByMaxes([ended.Fight.Max], nextMax))   // a retry
            return ended.Decided is { } was && now - ended.At <= RetryWithin ? was with { Outcome = WatchOutcome.Failed } : null;
        if (ended.Decided is not null || WentHidden(ended.Fight)) return null;
        var next = RaidData.ByHp(gates, nextMax)?.Gate;
        bool nextGate = next is not null && ended.Fight.Gate is { } gate && next.Raid == gate.Raid && next.Id != gate.Id;
        return Finishing(ended.Fight, OutcomeAt(ended.Fight, nextGate), raids);
    }

    /// <summary>What the bar's last HP says: a kill is a clear when the bar just went or the next gate came, else (another boss came)
    /// 확인필요; 5 to 10% is a clear only with the next gate, else 확인필요; more is no end (null).</summary>
    static WatchOutcome? OutcomeAt(WatchFight f, bool? nextGate)
    {
        bool killed = FightRecorder.Killed(f.Current, f.Max);
        if (!killed && f.Current >= f.Max * MidShare) return null;
        return nextGate == true || killed && nextGate is null ? WatchOutcome.Cleared : WatchOutcome.Unsure;
    }

    /// <summary>The decision when <paramref name="f"/>'s gate finishes a raid at its difficulty. Without a known difficulty it is
    /// 확인필요, with the one level that could end there, else none for the service to fill.</summary>
    static WatchDecision? Finishing(WatchFight f, WatchOutcome? outcome, IReadOnlyList<HomeworkRaid> raids)
    {
        if (outcome is not { } o || f.Gate is not { } gate || raids.FirstOrDefault(r => r.Gates.Contains(gate.Id)) is not { } raid) return null;
        var levels = raid.Levels.Where(l => l.Last.Contains(gate.Id) && !l.EndsHidden).ToList();
        if (f.Difficulty is { } d) return levels.Any(l => l.Difficulty == d) ? new WatchDecision(raid.Id, d, o) : null;
        return levels.Count == 0 ? null : new WatchDecision(raid.Id, levels.Count == 1 ? levels[0].Difficulty : null, WatchOutcome.Unsure);
    }

    static WatchDecision? HiddenEnd(WatchFight f, IReadOnlyList<HomeworkRaid> raids)
    {
        if (f.Gate is not { } gate || raids.FirstOrDefault(r => r.Gates.Contains(gate.Id)) is not { } raid) return null;
        var levels = raid.Levels.Where(l => l.EndsHidden && l.Last.Contains(gate.Id) && (f.Difficulty is null || l.Difficulty == f.Difficulty)).ToList();
        return levels.Count == 0 ? null : new WatchDecision(raid.Id, levels.Count == 1 ? levels[0].Difficulty : null, WatchOutcome.Unsure);
    }
}
