using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>A boss max HP learned for one difficulty (settings.json, wins over raids.json). <paramref name="Replaced"/> is the
/// raids.json value it replaced (null: raids.json had none), so an update that changes raids.json's value drops it, unless
/// <paramref name="Fixed"/> (확정 in 설정: kept over any raids.json). <paramref name="Raids"/>: how many raids read it (an older
/// file's reads as 1).</summary>
public sealed record LearnedHp(long Hp, long? Replaced, int Raids = 1, bool Fixed = false);

/// <summary>A first value (<see cref="HpTrust.New"/>) seen in one raid, saved as learned once a later raid reads it again.
/// <paramref name="Raids"/>: how many raids read it so far.</summary>
public sealed record PendingHp(long Hp, DateTime Seen, int Raids = 1);   // Seen: UTC

/// <summary>A row of HP 변경 감지's list: a value found to differ from raids.json (<paramref name="From"/>: its value, null for a
/// difficulty it has none for), how many raids read it, and whether it is still a pending first value.</summary>
public sealed record HpFound(RaidGate Gate, string Difficulty, long? From, long To, int Raids, bool Pending);

/// <summary>A row of the boss HP table: raids.json's value and the 확정 one, where there is one.</summary>
public sealed record HpTableRow(RaidGate Gate, string Difficulty, long? Shipped, long? Fixed);

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
    public const double Near = 0.30, NearWithoutPanel = 0.15, MinChange = 0.01;
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
        if (Off(hp, stored) < MinChange) return null;   // a patch moves HP ~15%; a few points off is a misread (user, 2026-11-09)
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
                if (g.Hp.TryGetValue(d, out var now) && (now == l.Hp || now != l.Replaced && !l.Fixed)) dropped |= s.LearnedHp!.Remove(d);
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
    /// value replaces the pending one. A value not in <paramref name="before"/> (this raid's list at the last call; null: count
    /// nothing) is a raid that read it, counted on the stored value. True when something changed (save).
    /// ponytail: a raid is a new list from the engine (HpLearner starts over on a new panel); a panel re-read mid-raid counts twice.</summary>
    public static bool Keep(IReadOnlyList<RaidGate> shipped, RaidSettings raids, IReadOnlyList<FightHp> fight, DateTime now,
        IReadOnlyList<FightHp>? before = null)
    {
        bool New(FightHp f) => before is not null && !before.Any(b => b.GateId == f.GateId && b.Difficulty == f.Difficulty && b.Hp == f.Hp);
        bool added = false;
        foreach (var f in fight.Where(f => f.Trust == HpTrust.New))
        {
            var row = raids.Gate(f.GateId);
            var pending = row.PendingHp ??= [];
            if (pending.GetValueOrDefault(f.Difficulty) is { } p && p.Hp == f.Hp)
            {
                int seen = p.Raids + (New(f) ? 1 : 0);
                if (now - p.Seen >= LaterRaid)
                {
                    pending.Remove(f.Difficulty);
                    (row.LearnedHp ??= [])[f.Difficulty] = new(f.Hp, null, seen);
                }
                else if (seen != p.Raids) pending[f.Difficulty] = p with { Raids = seen };
                else continue;
            }
            else pending[f.Difficulty] = new(f.Hp, now);
            added = true;
        }
        foreach (var f in fight.Where(f => f.Confirmed))
        {
            if (RaidData.Find(shipped, f.GateId) is not { } g) continue;
            var learned = raids.Gate(f.GateId).LearnedHp ??= [];
            var was = learned.GetValueOrDefault(f.Difficulty);
            // raids.json's own value read again (Judge saw it near the learned one): back to raids.json, not a change to list.
            if (g.Hp.TryGetValue(f.Difficulty, out var shippedHp) && shippedHp == f.Hp)
            {
                if (was is not null && !was.Fixed) added |= learned.Remove(f.Difficulty);
                continue;
            }
            var next = was is { } w && w.Hp == f.Hp
                ? w with { Raids = w.Raids + (New(f) ? 1 : 0) }
                : new LearnedHp(f.Hp, g.Hp.TryGetValue(f.Difficulty, out var ship) ? ship : null);
            if (was == next) continue;
            learned[f.Difficulty] = next;
            added = true;
        }
        return added;
    }

    /// <summary>HP 변경 감지's list: the learned values not 확정 yet and the pending first values.</summary>
    public static IReadOnlyList<HpFound> Found(IReadOnlyList<RaidGate> shipped, RaidSettings raids) =>
        [.. shipped.SelectMany(g => raids.Gates.GetValueOrDefault(g.Id) is not { } s ? [] :
            (s.LearnedHp ?? []).Where(l => !l.Value.Fixed).Select(l => new HpFound(g, l.Key, l.Value.Replaced, l.Value.Hp, l.Value.Raids, false))
            .Concat((s.PendingHp ?? []).Select(p => new HpFound(g, p.Key, g.Hp.TryGetValue(p.Key, out var was) ? was : null, p.Value.Hp, p.Value.Raids, true))))];

    /// <summary>확정: the found value is kept over raids.json from now on (a pending one is learned at once). True when changed.</summary>
    public static bool Confirm(IReadOnlyList<RaidGate> shipped, RaidSettings raids, string gateId, string difficulty)
    {
        if (raids.Gates.GetValueOrDefault(gateId) is not { } s) return false;
        if (s.LearnedHp?.GetValueOrDefault(difficulty) is { } l)
        {
            if (l.Fixed) return false;
            s.LearnedHp[difficulty] = l with { Fixed = true };
            return true;
        }
        if (s.PendingHp?.GetValueOrDefault(difficulty) is not { } p) return false;
        s.PendingHp.Remove(difficulty);
        long? was = RaidData.Find(shipped, gateId) is { } g && g.Hp.TryGetValue(difficulty, out var h) ? h : null;
        (s.LearnedHp ??= [])[difficulty] = new(p.Hp, was, p.Raids, Fixed: true);
        return true;
    }

    /// <summary>A value typed into the boss HP table: kept as 확정; raids.json's own value goes back to it (되돌리기); no HP (≤ 0)
    /// is refused. True when changed.</summary>
    public static bool SetFixed(IReadOnlyList<RaidGate> shipped, RaidSettings raids, string gateId, string difficulty, long hp)
    {
        if (hp <= 0) return false;
        long? was = RaidData.Find(shipped, gateId) is { } g && g.Hp.TryGetValue(difficulty, out var h) ? h : null;
        if (was == hp) return Undo(raids, gateId, difficulty);
        var s = raids.Gate(gateId);
        var old = s.LearnedHp?.GetValueOrDefault(difficulty);
        if (old is { Fixed: true } && old.Hp == hp) return false;
        s.PendingHp?.Remove(difficulty);
        (s.LearnedHp ??= [])[difficulty] = new(hp, was, old?.Raids ?? 1, Fixed: true);
        return true;
    }

    /// <summary>An HP typed into the table: digits (commas and spaces allowed) or the game's units ("1조 2985억", "7629억 5000만").
    /// Null for anything else or a number too large.</summary>
    public static long? ParseHp(string text)
    {
        string t = text.Replace(",", "").Replace(" ", "");
        if (t.Length > 0 && t.All(char.IsAsciiDigit)) return long.TryParse(t, out var n) ? n : null;
        var m = Units.Match(t);
        if (!m.Success || t.Length == 0) return null;
        try
        {
            long Part(int i, long unit) => m.Groups[i].Success ? checked(long.Parse(m.Groups[i].Value) * unit) : 0;
            return checked(Part(1, 1_000_000_000_000) + Part(2, 100_000_000) + Part(3, 10_000));
        }
        catch (OverflowException) { return null; }
    }

    static readonly System.Text.RegularExpressions.Regex Units = new(@"^(?:([0-9]{1,7})조)?(?:([0-9]{1,4})억)?(?:([0-9]{1,4})만)?$");   // ASCII only: long.Parse takes no other digits

    /// <summary>되돌리기 (or 삭제): the gate's difficulty goes back to raids.json's value. True when something was dropped.</summary>
    public static bool Undo(RaidSettings raids, string gateId, string difficulty) =>
        raids.Gates.GetValueOrDefault(gateId) is { } s && ((s.LearnedHp?.Remove(difficulty) ?? false) | (s.PendingHp?.Remove(difficulty) ?? false));

    /// <summary>The boss HP table: every gate with HP, each difficulty raids.json or a 확정 has, in raids.json order.</summary>
    public static IReadOnlyList<HpTableRow> Table(IReadOnlyList<RaidGate> shipped, RaidSettings raids) =>
        [.. shipped.SelectMany(g =>
        {
            var fixedHp = (raids.Gates.GetValueOrDefault(g.Id)?.LearnedHp ?? []).Where(l => l.Value.Fixed).ToDictionary(l => l.Key, l => l.Value.Hp);
            return g.Hp.Keys.Concat(fixedHp.Keys.Where(d => !g.Hp.ContainsKey(d))).Select(d =>
                new HpTableRow(g, d, g.Hp.TryGetValue(d, out var h) ? h : null, fixedHp.TryGetValue(d, out var f) ? f : null));
        })];

    /// <summary>The gate's HP as raids.json writes it, for 설정's copy button.</summary>
    public static string HpLine(RaidGate gate) =>
        "\"hp\": { " + string.Join(", ", gate.Hp.Select(h => $"\"{h.Key}\": {h.Value}")) + " }";
}
