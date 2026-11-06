using System.Text.RegularExpressions;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>A player's figures in a region: 피해량 dealt in it and its average per second.</summary>
public sealed record RegionFigures(string Name, long Damage, double Dps);

/// <summary>A part of a fight on one boss bar: its name (2-1, or 1구간), seconds on the fight clock, whether the bar shows ??? there,
/// the boss HP lost in it (null under ???), and each player's figures, me first.</summary>
public sealed record FightRegion(string Name, double From, double To, bool Hidden, long? BossTaken, IReadOnlyList<RegionFigures> Players);

/// <summary>The timeline's regions (user, 2026-11-04): a fight's parts by the max HP its bar showed, the files of one run joined back
/// together. A gate's kill ends the file but not the 전분 clock or its damage, which is how the next file is known to go on. Pure.</summary>
public static partial class FightRegions
{
    /// <summary>A max this small is no boss: ??? reads as a stray digit (종막 EX 2-3: "7"). Below every real gate's HP.</summary>
    const long HiddenUnder = 100_000_000;
    const int MinReads = 3;   // a new max read this many times in a row is a new bar, not a misread
    const double SameHp = 0.001;   // the read wobbles in its last digits (FightVerdict)
    const double MaxClockGap = 30;   // fight seconds from one file's end to the next's start (3 s on 2026-11-04)
    static readonly TimeSpan MaxWallGap = TimeSpan.FromMinutes(10);   // the cutscene between them (45 s on 2026-11-04)

    /// <summary>The bar shows ???: what was read is no HP.</summary>
    public static bool IsHidden(long max) => max < HiddenUnder;

    /// <summary>The fight's last reads were ???, after a real bar: its last phase was reached.</summary>
    public static bool EndedHidden(IReadOnlyList<RecordBoss> boss)
    {
        var maxes = boss.Select(b => b.Max).OfType<long>().ToList();
        return maxes.Count > MinReads && maxes.TakeLast(MinReads).All(IsHidden) && maxes.Any(m => !IsHidden(m));
    }

    /// <summary>The fight's regions, in order; none on a single bar (nothing to tell apart).</summary>
    public static IReadOnlyList<FightRegion> Of(FightRecord r, IReadOnlyList<RaidGate> gates)
    {
        var bars = Bars(r.Boss);
        if (bars.Count < 2) return [];
        double first = r.Boss.Select(b => b.T).Concat(r.Samples.Select(s => s.T)).Min();
        var names = Names(r, bars, gates);
        var taken = r.Boss.OrderBy(b => b.T).Select(b => (b.T, (double)b.Taken)).ToList();
        var players = r.Samples.GroupBy(s => s.Name).Select(g => (g.Key, Line: g.OrderBy(s => s.T).Select(s => (s.T, (double)s.Damage)).ToList())).ToList();
        return [.. bars.Select((bar, i) =>
        {
            double from = i == 0 ? first : bar.From, to = i + 1 < bars.Count ? bars[i + 1].From : Math.Max(r.Duration, from);
            long Between(List<(double T, double V)> line) => (long)Math.Round(Timeline.At(line, to) - Timeline.At(line, from));
            double seconds = Math.Max(1, to - from);
            var figures = players.Select(p => new RegionFigures(p.Key, Between(p.Line), Between(p.Line) / seconds))
                .OrderBy(f => f.Name != r.Me).ThenByDescending(f => f.Damage).ToList();
            return new FightRegion(names[i], from, to, IsHidden(bar.Max), IsHidden(bar.Max) ? null : Between(taken), figures);
        })];
    }

    /// <summary>The bars a fight showed: each max from where it first read, a new one only once it held for <see cref="MinReads"/>.</summary>
    static List<(double From, long Max)> Bars(IReadOnlyList<RecordBoss> boss)
    {
        var bars = new List<(double From, long Max)>();
        (double From, long Max, int Reads)? next = null;
        foreach (var b in boss.Where(b => b.Max > 0).OrderBy(b => b.T))
        {
            long max = b.Max!.Value;
            if (bars.Count == 0) { bars.Add((b.T, max)); continue; }
            if (Same(max, bars[^1].Max)) { next = null; continue; }
            next = next is { } n && Same(max, n.Max) ? n with { Reads = n.Reads + 1 } : (b.T, max, 1);
            if (next.Value.Reads < MinReads) continue;
            bars.Add((next.Value.From, next.Value.Max));
            next = null;
        }
        return bars;
    }

    static bool Same(long a, long b) => IsHidden(a) || IsHidden(b) ? IsHidden(a) && IsHidden(b) : Math.Abs(a - b) <= a * SameHp;

    /// <summary>"2-1" by the gate its HP is; a bar without HP data takes the next number after the one before it (하드 2-2, every
    /// 2-3); when any can't be named this way, all are numbered, so 1구간 never sits beside 2-2.</summary>
    static string[] Names(FightRecord r, List<(double From, long Max)> bars, IReadOnlyList<RaidGate> gates)
    {
        var names = new string?[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            var gate = RaidData.ByHp(gates, bars[i].Max)?.Gate ?? (i == 0 ? gates.FirstOrDefault(g => RaidData.Label(g) == r.Gate) : null);
            names[i] = gate is not null ? Short(gate.Gate) : i > 0 && names[i - 1] is { } before ? After(before) : null;
        }
        return names.All(n => n is not null) ? [.. names.OfType<string>()] : [.. bars.Select((_, i) => $"{i + 1}구간")];
    }

    /// <summary>"EX 2-1관문" / "2-2관문 (하드)" → "2-1"; null for a gate with no parts ("1관문").</summary>
    static string? Short(string gate) => Part().Match(gate) is { Success: true } m ? m.Value : null;

    static string? After(string name) =>
        Part().Match(name) is { Success: true } m ? $"{m.Groups[1].Value}-{int.Parse(m.Groups[2].Value) + 1}" : null;

    [GeneratedRegex(@"(\d+)-(\d+)")]
    private static partial Regex Part();

    /// <summary>The run <paramref name="opened"/> is part of, in order, from <paramref name="all"/> (give it one list only: mine, or
    /// the imported ones): each next file by the same player where the clock and the damage go on.</summary>
    public static IReadOnlyList<FightRecord> Chain(FightRecord opened, IEnumerable<FightRecord> all)
    {
        var fights = all.Where(f => f.Me == opened.Me && f.Character == opened.Character && f.Samples.Count > 0).OrderBy(f => f.StartedAt).ToList();
        var chain = new List<FightRecord> { opened };
        for (var at = opened; fights.LastOrDefault(f => GoesOn(f, at)) is { } before; at = before) chain.Insert(0, before);
        for (var at = opened; fights.FirstOrDefault(f => GoesOn(at, f)) is { } next; at = next) chain.Add(next);
        return chain;
    }

    static bool GoesOn(FightRecord a, FightRecord b)
    {
        if (a.StartedAt == b.StartedAt || a.Samples.Count == 0 || b.Samples.Count == 0) return false;
        double aFirst = a.Samples.Min(s => s.T), bFirst = b.Samples.Min(s => s.T), gap = bFirst - a.Duration;
        if (gap < 0 || gap > MaxClockGap) return false;
        var ended = a.StartedAt + TimeSpan.FromSeconds(a.Duration - aFirst);
        if (b.StartedAt < a.StartedAt || b.StartedAt - ended > MaxWallGap) return false;
        return a.Samples.LastOrDefault(s => s.Name == a.Me) is { } was && b.Samples.FirstOrDefault(s => s.Name == b.Me) is { } now && now.Damage >= was.Damage;
    }

    /// <summary>The run as one fight, to draw: the first's start, the last's end and result. One file comes back as it is.</summary>
    public static FightRecord Join(IReadOnlyList<FightRecord> chain)
    {
        if (chain.Count == 1) return chain[0];
        var (head, last) = (chain[0], chain[^1]);
        return head with
        {
            Gate = JoinedName(head.Gate, last.Gate),
            Duration = last.Duration,
            Phases = [.. chain.SelectMany(f => f.Phases)],
            Members = [.. chain.SelectMany(f => f.Members).DistinctBy(m => m.Name)],
            Samples = [.. chain.SelectMany(f => f.Samples)],
            Boss = ContinuedBoss(chain),
            Events = [.. chain.SelectMany(f => f.Events)],
            Cleared = last.Cleared,
            AmsuOn = chain.Any(f => f.AmsuOn),
        };
    }

    /// <summary>The files' boss reads as one line: a file starting under half of where the last one ended restarted its count, so it
    /// goes on from there; a smaller dip is a heal or a wobble of the same count. Everything that subtracts
    /// <see cref="RecordBoss.Taken"/> over a joined run needs this.</summary>
    static List<RecordBoss> ContinuedBoss(IReadOnlyList<FightRecord> chain)
    {
        var joined = new List<RecordBoss>();
        long shift = 0, ended = 0;
        foreach (var file in chain.Where(f => f.Boss.Count > 0))
        {
            if (file.Boss[0].Taken < ended / 2) shift += ended;
            joined.AddRange(file.Boss.Select(b => b with { Taken = b.Taken + shift }));
            ended = file.Boss[^1].Taken;
        }
        return joined;
    }

    /// <summary>"종막 · EX 2-1관문" and "종막 · EX 2-2관문" → "종막 · EX 2-1관문 ~ EX 2-2관문".</summary>
    static string? JoinedName(string? first, string? last)
    {
        if (first is null || last is null || first == last) return first ?? last;
        const string Sep = " · ";
        int at = last.IndexOf(Sep, StringComparison.Ordinal);
        return $"{first} ~ {(at >= 0 && first.StartsWith(last[..(at + Sep.Length)], StringComparison.Ordinal) ? last[(at + Sep.Length)..] : last)}";
    }
}
