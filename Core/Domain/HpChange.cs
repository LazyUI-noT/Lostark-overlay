using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>A boss max HP learned for one difficulty (settings.json, wins over raids.json). <paramref name="Replaced"/> is the
/// raids.json value it replaced (null: raids.json had none), so an update that changes raids.json's value drops it.</summary>
public sealed record LearnedHp(long Hp, long? Replaced);

/// <summary>A first value (<see cref="HpTrust.New"/>) seen in one raid, saved as learned once a later raid reads it again.</summary>
public sealed record PendingHp(long Hp, DateTime Seen);   // Seen: UTC

/// <summary>How far a value this raid reads is trusted. Unconfirmed: near a stored one without the panel, this raid only.
/// New: far from every stored one, the panel naming a difficulty the gate has none for; pending until a later raid. Confirmed:
/// near a stored one and the panel agrees; saved.</summary>
public enum HpTrust { Unconfirmed, New, Confirmed }

/// <summary>A value this raid uses (saved outside 테스트 모드 as its <see cref="Trust"/> allows).</summary>
public sealed record FightHp(string GateId, string Difficulty, long Hp, HpTrust Trust)
{
    public bool Confirmed => Trust == HpTrust.Confirmed;
}

/// <summary>The max HP read the same <see cref="HpChange.Reads"/> times in a row.</summary>
public sealed record HpStreak(long? Hp, int Count)
{
    public static readonly HpStreak None = new(null, 0);
    public bool Steady => Hp is not null && Count >= HpChange.Reads;
    public HpStreak Next(long? hp) => hp is null ? None : hp == Hp ? this with { Count = Count + 1 } : new(hp, 1);
}

/// <summary>HP 변경 감지 over one raid's name steps (Engine.Raid): the max HP streak, the HP showing when name reads (not the HP)
/// named the recognised gate and how many did, the values this raid uses, and the dungeon panel they were learned under.</summary>
public sealed record HpLearner(HpStreak Streak, long? NamedHp, int NamedReads, IReadOnlyList<FightHp> Fight, DungeonInfo? Panel)
{
    public static readonly HpLearner None = new(HpStreak.None, null, 0, [], null);
    /// <summary>Name reads a first value (<see cref="HpTrust.New"/>) needs while its HP shows; one for the others.</summary>
    public const int NewNameReads = 2;

    /// <summary>One name step. Another panel (a new dungeon or difficulty, the panel gone after its forget time, a guardian
    /// raid) starts over; a panel never read doesn't, so an unconfirmed value stays put. Once the max HP read the same 3 times
    /// in a row and a name read named the recognised gate while it showed, a value HpChange.Judge accepts (against the confirmed
    /// values only, so the panel can later confirm an unconfirmed one) joins <see cref="Fight"/> as a new list.</summary>
    public HpLearner Step(IReadOnlyList<RaidGate> shipped, RaidSettings raids, string? recognised, long? maxHp, string? byName, DungeonInfo? panel)
    {
        var l = panel == Panel ? this : None with { Panel = panel };
        var streak = l.Streak.Next(maxHp);
        if (byName is not null && byName == recognised)   // each name read comes once (Engine.NameByOcr takes it)
            l = maxHp == l.NamedHp ? l with { NamedReads = l.NamedReads + 1 } : l with { NamedHp = maxHp, NamedReads = 1 };
        l = l with { Streak = streak };
        if (!streak.Steady || l.NamedHp != streak.Hp || streak.Hp is not { } hp) return l;
        string? difficulty = panel is { Kind: DungeonKind.Raid } p ? p.Difficulty : null;
        var gate = RaidData.Find(HpChange.Effective(shipped, raids, [.. l.Fight.Where(f => f.Confirmed)]), recognised);
        if (gate is null || panel is { Kind: not DungeonKind.Raid } || HpChange.Judge(gate, hp, difficulty) is not ({ } d, var trust)
            || trust == HpTrust.New && l.NamedReads < NewNameReads) return l;
        var add = new FightHp(gate.Id, d, hp, trust);
        return l.Fight.Contains(add) ? l : l with { Fight = [.. l.Fight.Where(f => f.GateId != gate.Id || f.Difficulty != d), add] };
    }
}

/// <summary>HP-change learning (hp-anchor.plan.md step 3): a game patch changes a boss's max HP (nerfs ~15%, rarely 25%) while a
/// raid's difficulties lie 40–60% apart, so a read value near one stored difficulty is that difficulty with a new HP. Pure.</summary>
public static class HpChange
{
    public const double Near = 0.30, NearWithoutPanel = 0.15;
    public const int Reads = 3;

    /// <summary>What <paramref name="hp"/> is for the gate the name picked: (difficulty, trust), or null when there's nothing
    /// to learn. Near exactly one stored value: the panel agreeing confirms it; no panel and within 15% uses it unconfirmed.
    /// Near none: the panel's difficulty, when the gate has no value for it yet (a first value). A disagreeing panel or two
    /// near values: null.</summary>
    public static (string Difficulty, HpTrust Trust)? Judge(RaidGate gate, long hp, string? panelDifficulty)
    {
        if (gate.Hp.Values.Contains(hp)) return null;
        var near = gate.Hp.Where(h => Off(hp, h.Value) <= Near).ToList();
        if (near.Count > 1) return null;
        if (near.Count == 0)
            return panelDifficulty is { } d && !gate.Hp.ContainsKey(d) ? (d, HpTrust.New) : null;
        var (difficulty, stored) = near[0];
        if (panelDifficulty is null) return Off(hp, stored) <= NearWithoutPanel ? (difficulty, HpTrust.Unconfirmed) : null;
        return panelDifficulty == difficulty ? (difficulty, HpTrust.Confirmed) : null;
    }

    static double Off(long hp, long stored) => Math.Abs(hp - stored) / (double)stored;

    /// <summary>The gates with the learned values and this fight's merged into their HP, so RaidData.ByHp and HiddenHp use them.</summary>
    public static IReadOnlyList<RaidGate> Effective(IReadOnlyList<RaidGate> shipped, RaidSettings raids, IReadOnlyList<FightHp> fight) =>
        [.. shipped.Select(g =>
        {
            var learned = raids.Gates.TryGetValue(g.Id, out var s) ? s.LearnedHp : null;
            var mine = fight.Where(f => f.GateId == g.Id).ToList();
            if (learned is not { Count: > 0 } && mine.Count == 0) return g;
            var hp = new Dictionary<string, long>(g.Hp);
            foreach (var (d, l) in learned ?? []) hp[d] = l.Hp;
            foreach (var f in mine) hp[f.Difficulty] = f.Hp;
            return g with { HpByDifficulty = hp };
        })];

    /// <summary>Drops each learned value raids.json now has a value for: the learned one itself, or a newer one than the value it
    /// replaced; and each pending value raids.json now has one for. True when something was dropped (save).</summary>
    public static bool Prune(IReadOnlyList<RaidGate> shipped, RaidSettings raids)
    {
        bool dropped = false;
        foreach (var g in shipped)
        {
            if (!raids.Gates.TryGetValue(g.Id, out var s)) continue;
            foreach (var (d, l) in s.LearnedHp?.ToList() ?? [])
                if (g.Hp.TryGetValue(d, out var now) && (now == l.Hp || now != l.Replaced)) dropped |= s.LearnedHp!.Remove(d);
            foreach (var d in s.PendingHp?.Keys.ToList() ?? [])
                if (g.Hp.ContainsKey(d)) dropped |= s.PendingHp!.Remove(d);
        }
        return dropped;
    }

    /// <summary>A later raid, for a first value: one raid (wipes and all) rarely lasts this long.
    /// ponytail: a time gap stands for "another entry into the raid"; count raid entries if a raid ever runs longer.</summary>
    public static readonly TimeSpan LaterRaid = TimeSpan.FromHours(6);

    /// <summary>Stores this raid's values (not in 테스트 모드: the caller decides). A confirmed one is learned with the raids.json
    /// value it replaces. A first value is pending, and learned when a raid <see cref="LaterRaid"/> later reads it again; another
    /// value replaces the pending one. True when something changed (save).</summary>
    public static bool Keep(IReadOnlyList<RaidGate> shipped, RaidSettings raids, IReadOnlyList<FightHp> fight, DateTime now)
    {
        bool added = false;
        foreach (var f in fight.Where(f => f.Trust == HpTrust.New))
        {
            var row = raids.Gate(f.GateId);
            var pending = row.PendingHp ??= [];
            if (pending.GetValueOrDefault(f.Difficulty) is { } p && p.Hp == f.Hp)
            {
                if (now - p.Seen < LaterRaid) continue;
                pending.Remove(f.Difficulty);
                (row.LearnedHp ??= [])[f.Difficulty] = new(f.Hp, null);
            }
            else pending[f.Difficulty] = new(f.Hp, now);
            added = true;
        }
        foreach (var f in fight.Where(f => f.Confirmed))
        {
            if (RaidData.Find(shipped, f.GateId) is not { } g) continue;
            var learned = raids.Gate(f.GateId).LearnedHp ??= [];
            var next = new LearnedHp(f.Hp, g.Hp.TryGetValue(f.Difficulty, out var was) ? was : null);
            if (learned.GetValueOrDefault(f.Difficulty) == next) continue;
            learned[f.Difficulty] = next;
            added = true;
        }
        return added;
    }

    /// <summary>The gate's HP as raids.json writes it, for 설정's copy button.</summary>
    public static string HpLine(RaidGate gate) =>
        "\"hp\": { " + string.Join(", ", gate.Hp.Select(h => $"\"{h.Key}\": {h.Value}")) + " }";
}
