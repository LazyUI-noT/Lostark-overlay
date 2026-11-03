namespace GaugeOverlay.Core.Domain;

/// <summary>The seconds the timeline shows, <see cref="From"/> to <see cref="To"/> on the fight clock.</summary>
public sealed record TimeWindow(double From, double To)
{
    /// <summary>The narrowest zoom, in seconds.</summary>
    public const double MinSpan = 5;

    public double Span => To - From;

    public static TimeWindow Whole(double length) => new(0, Math.Max(length, MinSpan));

    public double X(double t, double width) => (t - From) / Span * width;

    /// <summary>Span times <paramref name="factor"/>, keeping second <paramref name="at"/> where it is on screen; between
    /// <see cref="MinSpan"/> and the whole record.</summary>
    public TimeWindow Zoom(double factor, double at, double length)
    {
        double max = Math.Max(length, MinSpan), span = Math.Clamp(Span * factor, MinSpan, max);
        double from = at - (at - From) / Span * span;
        return Placed(from, span, max);
    }

    /// <summary>Moved by <paramref name="seconds"/>, within the record.</summary>
    public TimeWindow Pan(double seconds, double length) => Placed(From + seconds, Span, Math.Max(length, Span));

    static TimeWindow Placed(double from, double span, double max)
    {
        from = Math.Clamp(from, 0, max - span);
        return new(from, from + span);
    }
}

/// <summary>What the timeline draws for a record (spec § D Timeline view). Pure.</summary>
public static class Timeline
{
    /// <summary>Two fights 비교 can line up: the same known gate at the same difficulty (user, 2026-11-03).</summary>
    public static bool SameRaid(FightRecord a, FightRecord b) =>
        !string.IsNullOrEmpty(a.Gate) && a.Gate == b.Gate && a.Difficulty == b.Difficulty;

    /// <summary>How much bigger the skill icons draw at a zoom: the whole fight over the seconds shown, at most twice (user,
    /// 2026-11-03), so zooming in spreads the casts and enlarges them.</summary>
    public static double IconScale(double whole, double shown) => shown <= 0 ? 1 : Math.Clamp(whole / shown, 1, 2);

    /// <summary>The skills I used in the fight, in bar order: one row each.</summary>
    public static IReadOnlyList<SlotId> MySkills(FightRecord r) =>
        [.. r.Events.Where(e => e.Name == r.Me && e.Kind == RecordEventKind.사용 && Enum.IsDefined((SlotId)e.What))
            .Select(e => (SlotId)e.What).Distinct().Order()];

    /// <summary>The share of my damage in <paramref name="w"/> dealt while my <paramref name="kind"/> buff was up, as the DPS
    /// 미터기's 버프 적용률 counts it (MeterMath.Cover); 0 when I dealt none in it.</summary>
    public static double BuffShare(FightRecord r, BuffKind kind, TimeWindow w) =>
        DamageShare(r, BuffSpans(r).Where(s => s.Kind == kind).Select(s => (s.From, s.To)), w);

    /// <summary>The share of my damage in <paramref name="w"/> dealt while a bomb was on the boss, as <see cref="BuffShare"/>.</summary>
    public static double BombShare(FightRecord r, TimeWindow w) => DamageShare(r, AmsuSpans(r).Select(s => (s.From, s.To)), w);

    /// <summary>The share of the boss HP lost in <paramref name="w"/> while my <paramref name="kind"/> buff was up: the party's in a
    /// 4-man raid (MeterView.BuffParty), whose damage is all the boss takes; null when no boss HP was lost in it.</summary>
    public static double? PartyBuffShare(FightRecord r, BuffKind kind, TimeWindow w)
    {
        var boss = r.Boss.OrderBy(b => b.T).Select(b => (b.T, (double)b.Taken)).ToList();
        if (At(boss, w.To) - At(boss, w.From) <= 0) return null;
        return Share(boss, BuffSpans(r).Where(s => s.Kind == kind).Select(s => (s.From, s.To)), w);
    }

    static double DamageShare(FightRecord r, IEnumerable<(double From, double To)> spans, TimeWindow w) => Share(MyDamage(r), spans, w);

    static double Share(List<(double T, double V)> mine, IEnumerable<(double From, double To)> spans, TimeWindow w)
    {
        double total = At(mine, w.To) - At(mine, w.From);
        if (total <= 0) return 0;
        double under = spans.Where(s => s.To > w.From && s.From < w.To)
            .Sum(s => At(mine, Math.Min(s.To, w.To)) - At(mine, Math.Max(s.From, w.From)));
        return under / total;
    }

    /// <summary>Each player's average DPS over <paramref name="w"/> (their samples in it): me first, then highest first.</summary>
    public static IReadOnlyList<(string Name, double Dps)> DpsAverages(FightRecord r, TimeWindow w) =>
        [.. r.Samples.Where(s => s.T >= w.From && s.T <= w.To).GroupBy(s => s.Name).Select(g => (Name: g.Key, Dps: g.Average(s => (double)s.Dps)))
            .OrderBy(x => x.Name != r.Me).ThenByDescending(x => x.Dps)];

    public static IReadOnlyList<RecordEvent> Visible(FightRecord r, TimeWindow w) => [.. r.Events.Where(e => e.T >= w.From && e.T <= w.To)];

    /// <summary>My buff windows: each start to its end, or to the end of the fight when none came.</summary>
    public static IReadOnlyList<(double From, double To, BuffKind Kind)> BuffSpans(FightRecord r)
    {
        var spans = new List<(double, double, BuffKind)>();
        var open = new Dictionary<int, double>();
        foreach (var e in r.Events.Where(e => e.Name == r.Me).OrderBy(e => e.T))
        {
            if (e.Kind == RecordEventKind.버프시작) open.TryAdd(e.What, e.T);
            else if (e.Kind == RecordEventKind.버프끝 && open.Remove(e.What, out double from)) spans.Add((from, e.T, (BuffKind)e.What));
        }
        spans.AddRange(open.Select(o => (o.Value, r.Duration, (BuffKind)o.Key)));
        return [.. spans.OrderBy(s => s.Item1)];
    }

    /// <summary>The bomb windows (a bomb up on the boss): each start to its end, or to the end of the fight when none came.</summary>
    public static IReadOnlyList<(double From, double To, Vision.Bomb Kind)> AmsuSpans(FightRecord r)
    {
        var spans = new List<(double, double, Vision.Bomb)>();
        (double From, int What)? open = null;
        foreach (var e in r.Events.Where(e => e.Name == r.Me && e.Kind is RecordEventKind.암수시작 or RecordEventKind.암수끝).OrderBy(e => e.T))
        {
            if (open is { } o) spans.Add((o.From, e.T, (Vision.Bomb)o.What));
            open = e.Kind == RecordEventKind.암수시작 ? (e.T, e.What) : null;
        }
        if (open is { } last) spans.Add((last.From, r.Duration, (Vision.Bomb)last.What));
        return spans;
    }

    /// <summary>What a bomb adds to the damage dealt while it is up (user, 2026-11-01: a flat 10.35%).</summary>
    public const double AmsuShare = 0.1035;

    /// <summary>The 암수 gain over the fight: my 피해량 and the boss's damage taken within the bomb windows × <see cref="AmsuShare"/>,
    /// and my gain as a share of all my damage (0 when I dealt none); null when 암수 was not read in this fight (the windows
    /// would be missing, not empty).</summary>
    public static (double Mine, double Total, double MyShare)? AmsuGain(FightRecord r)
    {
        if (!r.AmsuOn) return null;
        var spans = AmsuSpans(r);
        var mine = MyDamage(r);
        var boss = r.Boss.OrderBy(b => b.T).Select(b => (b.T, (double)b.Taken)).ToList();
        double Within(List<(double T, double V)> line) => spans.Sum(s => At(line, s.To) - At(line, s.From));
        double gain = Within(mine) * AmsuShare, all = At(mine, r.Duration) - At(mine, 0);
        return (gain, Within(boss) * AmsuShare, all > 0 ? gain / all : 0);
    }

    /// <summary>My running 피해량 through the fight, for <see cref="At"/>.</summary>
    static List<(double T, double V)> MyDamage(FightRecord r) =>
        [.. r.Samples.Where(s => s.Name == r.Me).OrderBy(s => s.T).Select(s => (s.T, (double)s.Damage))];

    /// <summary>A running total at second <paramref name="t"/>: linear between samples, held past the ends.</summary>
    static double At(List<(double T, double V)> line, double t)
    {
        if (line.Count == 0) return 0;
        if (t <= line[0].T) return line[0].V;
        for (int i = 1; i < line.Count; i++)
            if (t <= line[i].T)
                return line[i - 1].V + (line[i].V - line[i - 1].V) * (t - line[i - 1].T) / Math.Max(1e-9, line[i].T - line[i - 1].T);
        return line[^1].V;
    }

    /// <summary>Where 비교 lines this record up: phase <paramref name="phase"/>'s start (0: the fight's), or the start when the
    /// record has no such phase.</summary>
    public static double Offset(FightRecord r, int phase) => phase >= 1 && phase <= r.Phases.Count ? r.Phases[phase - 1] : 0;

    /// <summary>The biggest 초당 피해량 and 피해량 among the samples within a second of the seconds shown (the curves draw those
    /// too): the two scales are fitted to these. Zero when none is near.</summary>
    public static (long Dps, long Damage) Peaks(FightRecord r, TimeWindow seen)
    {
        long dps = 0, damage = 0;
        foreach (var s in r.Samples)
        {
            if (s.T < seen.From - 1 || s.T > seen.To + 1) continue;
            dps = Math.Max(dps, s.Dps);
            damage = Math.Max(damage, s.Damage);
        }
        return (dps, damage);
    }
}
