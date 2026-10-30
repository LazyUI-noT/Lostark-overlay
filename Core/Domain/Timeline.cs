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
    /// <summary>The skills I used in the fight, in bar order: one row each.</summary>
    public static IReadOnlyList<SlotId> MySkills(FightRecord r) =>
        [.. r.Events.Where(e => e.Name == r.Me && e.Kind == RecordEventKind.사용 && Enum.IsDefined((SlotId)e.What))
            .Select(e => (SlotId)e.What).Distinct().Order()];

    /// <summary>The share of <paramref name="w"/> that my <paramref name="kind"/> buff was up (0 for an empty window).</summary>
    public static double Uptime(FightRecord r, BuffKind kind, TimeWindow w) =>
        w.Span <= 0 ? 0 : BuffSpans(r).Where(s => s.Kind == kind).Sum(s => Math.Max(0, Math.Min(s.To, w.To) - Math.Max(s.From, w.From))) / w.Span;

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

    /// <summary>Where 비교 lines this record up: phase <paramref name="phase"/>'s start (0: the fight's), or the start when the
    /// record has no such phase.</summary>
    public static double Offset(FightRecord r, int phase) => phase >= 1 && phase <= r.Phases.Count ? r.Phases[phase - 1] : 0;
}
