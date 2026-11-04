namespace GaugeOverlay.Core.Domain;

/// <summary>One read on the 보스 HP line: HP and the confirmed shield on top of it, as shares of the bar.</summary>
public sealed record BossHpPoint(double T, double Hp, double Shield);

/// <summary>A confirmed heal: from when HP began rising to its last rise, and what it gained, as a share of the bar.</summary>
public sealed record BossHeal(double From, double To, double Gain);

/// <summary>One bar (one max HP): its points and the heals in it.</summary>
public sealed record BossHpBar(long Max, IReadOnlyList<BossHpPoint> Points, IReadOnlyList<BossHeal> Heals);

/// <summary>The chart's 보스 HP: HP, heals and the shield stacked on top (user, 2026-11-04), with reads that make no sense left out,
/// because a saved fight opened on a read of "68" for a bar of 769,567,947,570. Only the chart uses this: damage and 보스 타격 불가
/// don't depend on it. Pure.</summary>
public static class BossHpGraph
{
    /// <summary>Faster than this a fall is not a believable read around a heal: the rate the compressed-video reader allows too.</summary>
    public const double MaxFallPerSecond = 0.05;

    /// <summary>One entry per bar (a new max starts another), none under ??? bars; a read without a max keeps the bar's.</summary>
    public static IReadOnlyList<BossHpBar> Of(FightRecord r)
    {
        var bars = new List<BossHpBar>();
        var reads = new List<RecordBoss>();
        long? bar = null, seen = null;
        void Finish() { if (bar is { } max && reads.Count > 0) bars.Add(Bar(reads, max)); reads = []; }
        foreach (var b in r.Boss.OrderBy(b => b.T))
        {
            if (b.Max is { } max && max > 0 && max != seen)
            {
                Finish();
                seen = max;
                bar = FightRegions.IsHidden(max) ? null : max;
            }
            if (bar is not null && b.Current is not null) reads.Add(b);
        }
        Finish();
        return bars;
    }

    /// <summary>An amount as the game's units spell it: "1조 571억", "571억", "5,000만".</summary>
    public static string AmountText(long hp)
    {
        long jo = hp / 1_000_000_000_000, eok = hp % 1_000_000_000_000 / 100_000_000;
        if (jo > 0) return eok > 0 ? $"{jo:N0}조 {eok:N0}억" : $"{jo:N0}조";
        return eok > 0 ? $"{eok:N0}억" : hp >= 10_000 ? $"{hp / 10_000:N0}만" : $"{hp:N0}";
    }

    /// <summary>The bar's "X n" for this HP: lines left, rounded up as the game shows them.</summary>
    public static int LinesLeft(long hp, double hpPerLine) => (int)Math.Ceiling(hp / hpPerLine);

    static BossHpBar Bar(IReadOnlyList<RecordBoss> reads, long max)
    {
        var raw = reads.Select(b => Math.Max(0, b.Shield ?? 0)).ToList();
        var shields = ConfirmedShields(raw);
        // HP from the read's own bracket, not the confirmed shield: a doubted shield must not look like HP moving.
        var points = reads.Select((b, i) => new BossHpPoint(b.T, (double)Math.Max(0, b.Current!.Value - raw[i]) / max, (double)shields[i] / max)).ToList();
        return new(max, points, Heals(points));
    }

    /// <summary>The shield each read counts: it appears on two reads in a row; grows (a new stack, any size) only if the next two reads
    /// stay above the old value; falls unless the next read snaps back; goes only after two reads without it.</summary>
    static long[] ConfirmedShields(IReadOnlyList<long> raw)
    {
        var shields = new long[raw.Count];
        long now = 0;
        for (int i = 0; i < raw.Count; i++)
        {
            long s = raw[i];
            long? next = i + 1 < raw.Count ? raw[i + 1] : null, after = i + 2 < raw.Count ? raw[i + 2] : null;
            if (now == 0) { if (s > 0 && next > 0) now = s; }
            else if (s == 0) { if (next is null or 0) now = 0; }
            else if (s > now) { if (next > now && after > now) now = s; }
            else if (s < now && (next is null || next <= s || next > now)) now = s;   // a next read back at the old value is a misread; above it, a new stack
            shields[i] = now;
        }
        return shields;
    }

    /// <summary>A rise in HP counts as a heal only between believable reads: two falling normally before it, two after its last rise.
    /// So a misread on either side draws nothing, and neither does a rise at the very start or end of a bar.</summary>
    static IReadOnlyList<BossHeal> Heals(IReadOnlyList<BossHpPoint> p)
    {
        var heals = new List<BossHeal>();
        for (int i = 0; i + 1 < p.Count; i++)
        {
            if (p[i + 1].Hp <= p[i].Hp) continue;
            int last = i + 1;
            for (int k = last + 1; k < p.Count && p[k].Hp >= p[k - 1].Hp; k++)
                if (p[k].Hp > p[last].Hp) last = k;
            if (i >= 1 && Normal(p[i - 1], p[i]) && last + 2 < p.Count && Normal(p[last], p[last + 1]) && Normal(p[last + 1], p[last + 2]))
                heals.Add(new(p[i].T, p[last].T, p[last].Hp - p[i].Hp));
            i = last - 1;
        }
        return heals;
    }

    static bool Normal(BossHpPoint a, BossHpPoint b) =>
        b.Hp <= a.Hp && a.Hp - b.Hp <= MaxFallPerSecond * Math.Max(1, b.T - a.T);
}
