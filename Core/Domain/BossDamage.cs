namespace GaugeOverlay.Core.Domain;

/// <summary>Boss damage taken so far: <see cref="Done"/> from finished segments, the current segment from
/// <see cref="SegmentStart"/> down to <see cref="Last"/> (the filtered HP) on a bar of <see cref="Max"/>, and the last two raw
/// reads for the filter. <see cref="LastShield"/> is the shield inside <see cref="Last"/>: the bracket of the same read, so the two
/// always subtract to a real bar value.</summary>
public sealed record BossDamageState(long Done, long? SegmentStart, long? Last, long? Max, long? Read1, long? Read2, long LastShield = 0, long Shield1 = 0, long Shield2 = 0)
{
    public static readonly BossDamageState None = new(0, null, null, null, null, null);
}

/// <summary>Boss damage taken since the 전분 record started (spec § C, the user's choice (a)): the HP bar's drop, summed over
/// segments. A new bar (another max: the next phase) or a rise within one (a shield, a heal) starts a segment from its first
/// value, so a record that starts mid-fight counts from the HP it saw first. Reads within a bar pass a median of three, so a
/// one-read misread neither counts nor splits; a phase is closed with its last raw read (often 0). The caller starts from
/// <see cref="BossDamageState.None"/> at each new 전분 record. Pure.</summary>
public static class BossDamage
{
    public static BossDamageState Update(BossDamageState s, (long Current, long Max)? hp) => Update(s, hp, 0);

    /// <summary>As <see cref="Update(BossDamageState, ValueTuple{long, long}?)"/> with a shield in brackets ("60/100(+40)"): it
    /// counts as HP (its appearing is a rise, its breaking is damage, as the 전분 counts it), so HP + shield may pass the max.</summary>
    public static BossDamageState Update(BossDamageState s, (long Current, long Max)? hp, long shield)
    {
        if (hp is not var (bar, max) || max <= 0 || bar < 0 || bar > max || shield < 0) return s;
        long cur = bar + shield;
        if (s.Max is not { } was || s.SegmentStart is not { } start || s.Last is not { } last) return new(s.Done, cur, cur, max, cur, null, shield, shield);
        if (max != was) return new(s.Done + start - Math.Min(last, s.Read1 ?? last), cur, cur, max, cur, null, shield, shield);
        long f = s.Read2 is { } r2 ? Median(r2, s.Read1!.Value, cur) : Math.Max(s.Read1 ?? cur, cur);
        long picked = f == cur ? shield : f == s.Read1 ? s.Shield1 : s.Shield2;
        var next = s with { Read2 = s.Read1, Read1 = cur, Shield2 = s.Shield1, Shield1 = shield, LastShield = picked };
        // A confirmed rise closes the segment at the last read before it (the filter lags a read behind a fall), as a phase does.
        return f > last ? next with { Done = s.Done + start - Math.Min(last, s.Read2 ?? last), SegmentStart = f, Last = f } : next with { Last = f };
    }

    public static long Taken(BossDamageState s) => s.Done + (s.SegmentStart is { } start && s.Last is { } last ? start - last : 0);

    static long Median(long a, long b, long c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
}
