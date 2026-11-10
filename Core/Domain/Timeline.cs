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
    /// <paramref name="minSpan"/> (the timeline's: Timeline.NarrowestSpan) and the whole record.</summary>
    public TimeWindow Zoom(double factor, double at, double length, double minSpan = MinSpan)
    {
        double max = Math.Max(length, MinSpan), span = Math.Clamp(Span * factor, Math.Min(minSpan, max), max);   // a short fight: its whole
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

    /// <summary>전투 기록's 레이드순 (user, 2026-11-10): one gate's fights together, its difficulties apart, each group where its newest
    /// fight stands in <paramref name="newestFirst"/>, newest first inside.</summary>
    public static IReadOnlyList<T> ByRaid<T>(IReadOnlyList<T> newestFirst, Func<T, string?> gate, Func<T, string?> difficulty) =>
        [.. newestFirst.GroupBy(gate).SelectMany(g => g.GroupBy(difficulty).SelectMany(d => d))];

    /// <summary>The window the boss must lose under a line in to count as unhittable (user, 2026-11-04).</summary>
    public const double UnhittableSeconds = 5;

    /// <summary>Where nobody could hit the boss (user, 2026-11-04), unlike a stretch where only my damage stops: under one line of the bar
    /// lost in <see cref="UnhittableSeconds"/>. <paramref name="hpPerLine"/> is the size of a line for a bar's max HP, null when not
    /// known (RaidData.HpPerLine). Not judged under a ??? bar, whose HP can't be read, nor the stall the fight ends on (a dead boss, a wiped party).</summary>
    public static IReadOnlyList<(double From, double To)> BossUnhittable(FightRecord r, Func<long, double?> hpPerLine)
    {
        var boss = r.Boss.OrderBy(b => b.T).ToList();
        var line = new double?[boss.Count];
        double? current = null;
        for (int i = 0; i < boss.Count; i++)
        {
            if (boss[i].Max is { } max) current = FightRegions.IsHidden(max) ? null : hpPerLine(max);   // a read without a max keeps the bar's
            line[i] = current;
        }
        var spans = new List<(double From, double To)>();
        for (int i = 0; i < boss.Count; i++)
        {
            if (line[i] is not { } size) continue;
            int j = i + 1;
            while (j < boss.Count && line[j] is not null && boss[j].T - boss[i].T < UnhittableSeconds) j++;
            if (j == boss.Count) break;
            long lost = boss[j].Taken - boss[i].Taken;
            if (line[j] is null || lost < 0 || lost >= size) continue;   // less than nothing: a heal, not a stall (a joined run's count goes on: FightRegions.Join)
            if (spans.Count > 0 && boss[i].T <= spans[^1].To) spans[^1] = (spans[^1].From, boss[j].T);
            else spans.Add((boss[i].T, boss[j].T));
        }
        if (spans.Count > 0 && spans[^1].To >= boss[^1].T) spans.RemoveAt(spans.Count - 1);
        return spans;
    }

    /// <summary>My casts of <paramref name="slot"/> a minute over the fight, with <paramref name="excluded"/> (보스 타격 불가) taken
    /// off: its time and the casts in it (user, 2026-11-09); null with no time left.</summary>
    public static double? SkillRate(FightRecord r, SlotId slot, IReadOnlyList<(double From, double To)> excluded)
    {
        double seconds = r.Duration - excluded.Sum(s => Math.Max(0, Math.Min(s.To, r.Duration) - Math.Max(s.From, 0)));
        return seconds <= 0 ? null : r.Events.Count(e => e.Name == r.Me && e.Kind == RecordEventKind.사용 && e.What == (int)slot
            && !excluded.Any(s => e.T >= s.From && e.T < s.To)) * 60 / seconds;
    }

    /// <summary>Where the figures beside the chart's line go, in the input's order: at their own height, pushed apart by
    /// <paramref name="gap"/> so none covers another, and kept between <paramref name="top"/> and <paramref name="bottom"/> as long as they fit.</summary>
    public static IReadOnlyList<double> SpreadTags(IReadOnlyList<double> ys, double gap, double top, double bottom)
    {
        var order = Enumerable.Range(0, ys.Count).OrderBy(i => ys[i]).ToList();
        var placed = new double[ys.Count];
        double next = top;
        foreach (int i in order) next = (placed[i] = Math.Max(ys[i], next)) + gap;
        double limit = bottom;
        foreach (int i in Enumerable.Reverse(order))
        {
            if (placed[i] <= limit) break;
            placed[i] = limit;
            limit -= gap;
        }
        return placed;
    }

    /// <summary><paramref name="spans"/> with <paramref name="zones"/> cut out: no 보스 타격 불가 inside 대난투, where the number shows
    /// the damage dealt and the boss is hit (user, 2026-11-04). A piece left under <see cref="UnhittableSeconds"/> goes too.</summary>
    public static IReadOnlyList<(double From, double To)> Outside(IReadOnlyList<(double From, double To)> spans, IReadOnlyList<(double From, double To)> zones)
    {
        var left = spans.ToList();
        foreach (var (zFrom, zTo) in zones)
            left = [.. left.SelectMany(s => s.To <= zFrom || s.From >= zTo ? [s]
                : new[] { (s.From, Math.Min(s.To, zFrom)), (Math.Max(s.From, zTo), s.To) }.Where(p => p.Item2 - p.Item1 >= UnhittableSeconds))];
        return left;
    }

    /// <summary>The stretches 대난투 may have held, for cutting 보스 타격 불가 out: its zones, each up to the boss bar's next HP read
    /// (between 종료 and the bar back nothing is read, which is no stall; user, 2026-11-05), and from each 입장 of an older fight,
    /// whose end wasn't recorded, to the fight's end.</summary>
    public static IReadOnlyList<(double From, double To)> GimmickSpans(FightRecord r, IReadOnlyList<(double T, bool Enter)> marks) =>
        GimmickZones(r) is { Count: > 0 } zones
            ? [.. zones.Select(z => (z.From, r.Boss.Where(b => b.T > z.To).Select(b => b.T).DefaultIfEmpty(z.To).Min()))]
            : [.. marks.Where(m => m.Enter).Select(m => (m.T, Math.Max(m.T, r.Duration)))];

    /// <summary>대난투's lines on the chart: its recorded start and end; a 그림자 gate's fight saved before they were recorded has only
    /// its timer stops (<see cref="FightRecord.Phases"/>), which came with the gimmick in every saved fight, as starts.</summary>
    public static IReadOnlyList<(double T, bool Enter)> GimmickMarks(FightRecord r, bool shadowGate)
    {
        var marks = r.Events.Where(e => e.Kind is RecordEventKind.대난투입장 or RecordEventKind.대난투종료).OrderBy(e => e.T)
            .Select(e => (e.T, e.Kind == RecordEventKind.대난투입장)).ToList();
        return marks.Count > 0 || !shadowGate ? marks : [.. r.Phases.Select(p => (p, true))];
    }

    /// <summary>대난투's zones, from its recorded start to its end, or the fight's end when it ended inside one. Only what was recorded: an
    /// older fight's end is not known.</summary>
    public static IReadOnlyList<(double From, double To)> GimmickZones(FightRecord r)
    {
        var zones = new List<(double, double)>();
        double? from = null;
        foreach (var e in r.Events.Where(e => e.Kind is RecordEventKind.대난투입장 or RecordEventKind.대난투종료).OrderBy(e => e.T))
        {
            if (e.Kind == RecordEventKind.대난투입장) from ??= e.T;
            else if (from is { } start) { zones.Add((start, e.T)); from = null; }
        }
        if (from is { } open) zones.Add((open, Math.Max(open, r.Duration)));
        return zones;
    }

    /// <summary>The time a cast's icon covers (user, 2026-11-05): it grows with the zoom, its left edge at the cast; fully zoomed the
    /// largest icon is this long (1 s at most, the user's last word).</summary>
    public const double IconSeconds = 1;

    /// <summary>An icon's size at this zoom: <see cref="IconSeconds"/> wide, between <paramref name="min"/> (zoomed out: the normal
    /// size, in 3 lanes) and <paramref name="max"/> (the skill row's height, one lane).</summary>
    public static double IconSize(double pxPerSecond, double min, double max) => Math.Clamp(IconSeconds * pxPerSecond, min, max);

    /// <summary>The narrowest zoom: where the largest icon is <see cref="IconSeconds"/> wide; never under TimeWindow.MinSpan.</summary>
    public static double NarrowestSpan(double plotWidth, double maxIcon) =>
        Math.Max(TimeWindow.MinSpan, plotWidth * IconSeconds / Math.Max(1, maxIcon));

    /// <summary>Each icon's lane (user, 2026-11-05: the 3 lanes, icons not crowded): as many lanes of <paramref name="size"/> as fit
    /// in <paramref name="full"/>, centred; a cast takes the first lane its icon (left edge <paramref name="lefts"/>, in time order)
    /// fits in, else goes over the oldest.</summary>
    public static IReadOnlyList<(double Top, double Size)> IconLanes(IReadOnlyList<double> lefts, double size, double gap, double full)
    {
        int lanes = Math.Max(1, (int)Math.Floor((full + gap) / (size + gap) + 1e-9));
        double top = (full - (lanes * size + (lanes - 1) * gap)) / 2;
        var last = Enumerable.Repeat(double.NegativeInfinity, lanes).ToArray();
        var places = new (double, double)[lefts.Count];
        for (int i = 0; i < lefts.Count; i++)
        {
            int lane = Array.FindIndex(last, l => lefts[i] - l >= size);
            if (lane < 0) lane = Array.IndexOf(last, last.Min());
            last[lane] = lefts[i];
            places[i] = (top + lane * (size + gap), size);
        }
        return places;
    }

    /// <summary>The icons under the hover line at <paramref name="x"/> (one per lane at most, more where a cast went over another),
    /// in time order: drawn over the others and named on the line. <paramref name="lefts"/>: left edges in time order, <paramref name="size"/> wide.</summary>
    public static IReadOnlyList<int> IconsUnder(IReadOnlyList<double> lefts, double size, double x) =>
        [.. Enumerable.Range(0, lefts.Count).Where(i => lefts[i] <= x && x < lefts[i] + size)];

    /// <summary>The skills I used in the fight, in bar order: one row each.</summary>
    public static IReadOnlyList<SlotId> MySkills(FightRecord r) =>
        [.. r.Events.Where(e => e.Name == r.Me && e.Kind == RecordEventKind.사용 && Enum.IsDefined((SlotId)e.What))
            .Select(e => (SlotId)e.What).Distinct().Order()];

    /// <summary>The share of my damage in <paramref name="w"/> dealt while my <paramref name="kind"/> buff was up, as the DPS
    /// 미터기's 버프 적용률 counts it (MeterMath.Cover); 0 when I dealt none in it.</summary>
    public static double BuffShare(FightRecord r, BuffKind kind, TimeWindow w) =>
        DamageShare(r, BuffSpans(r).Where(s => s.Kind == kind).Select(s => (s.From, s.To)), w);

    /// <summary>The share of <paramref name="w"/>'s time a bomb was on the boss (the 암수 line's %, user 2026-11-05).</summary>
    public static double AmsuUptime(FightRecord r, TimeWindow w) =>
        w.Span <= 0 ? 0 : AmsuSpans(r).Sum(s => Math.Max(0, Math.Min(s.To, w.To) - Math.Max(s.From, w.From))) / w.Span;

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

    /// <summary>My average DPS over the whole fight, as the timeline's legend has it when not zoomed (the 전투 기록 list beside 내 몫,
    /// user 2026-11-09); null when I have no figures.</summary>
    public static double? MyDps(FightRecord r) =>
        DpsAverages(r, TimeWindow.Whole(r.Duration)) is [var first, ..] && first.Name == r.Me ? first.Dps : null;

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
    internal static double At(List<(double T, double V)> line, double t)
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
