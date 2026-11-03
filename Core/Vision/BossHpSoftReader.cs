using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>테스트 모드 강화 읽기 of the boss HP (reinforced-reader.plan.md): <see cref="BossDigits.ReadHpWithShield"/> soft, checked
/// against the fight. On compressed video a blurred digit reads as its look-alike and the '/' fades into a mark; a boss's max
/// holds, and its HP only falls, by at most <see cref="MaxDropShare"/> of the max a second. A read that breaks either is no
/// read, unless the next read agrees with it (a new boss or phase). With no '/' read, the current is the digits before the
/// known max (the last of them the '/' read as a digit, or not), whichever fits the fight. One per boss bar.</summary>
public sealed class BossHpSoftReader
{
    const double MaxDropShare = 0.05;   // of the max, a second: far over a party's damage, under any high digit misread
    const double Agree = 0.001;         // of the max: two reads this near are the same HP

    (long Current, long Max, TimeSpan At)? _last;
    (long Current, long Max)? _odd;     // the last read that broke the fight: a second like it starts a new one

    /// <summary>The HP read from the bar's row as captured, or null. The row is cleaned three ways (<see cref="Reinforce"/>:
    /// sharpened, its levels stretched and sharpened, stretched only; each wins on some video), and the first read that fits
    /// the fight counts.</summary>
    public (long Current, long Max, long Shield)? Read(Image row, double scale, TimeSpan now)
    {
        var levels = new Lazy<Image>(() => Reinforce.Levels(row));
        var rows = new Lazy<Image>[] { new(() => Reinforce.Clean(row)), new(() => Reinforce.Clean(levels.Value)), levels };
        var reads = new List<(long, long, long)?>();
        foreach (var r in rows)   // cleaned one way at a time: the first that fits ends the read
        {
            var hp = BossDigits.ReadBar(r.Value, scale, soft: true).Hp;
            if (_last is { } fitsLast && hp is var (c, m, _) && Fits(fitsLast, c, m, now)) return Check(hp, now);
            reads.Add(hp);
        }
        if (_last is { } l)
            foreach (var r in rows)
                if (ByMax(BossDigits.DigitWords(r.Value, scale), l, now) is { } current) return Check((current, l.Max, 0), now);
        return Check(reads.FirstOrDefault(x => x is not null), now);
    }

    /// <summary>강화 turned on again: the next read starts the fight afresh.</summary>
    public void Reset() => (_last, _odd) = (null, null);

    /// <summary>The current HP from the row's digit words: the digits before the max's, whole or less the last (the '/'),
    /// whichever fits the fight and is nearer the last HP.</summary>
    internal static long? ByMax(IEnumerable<string> words, (long Current, long Max, TimeSpan At) last, TimeSpan now)
    {
        string max = last.Max.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var w in words)
            if (w.IndexOf(max, StringComparison.Ordinal) is var at and > 0
                && new[] { w[..at], w[..(at - 1)] }.Where(d => d.Length is > 0 and <= 18).Select(long.Parse)
                    .Where(c => Fits(last, c, last.Max, now)).OrderBy(c => last.Current - c).Cast<long?>().FirstOrDefault() is { } found)
                return found;
        return null;
    }

    internal (long Current, long Max, long Shield)? Check((long Current, long Max, long Shield)? read, TimeSpan now)
    {
        if (read is not var (current, max, shield)) return null;
        if (_last is { } l && !Fits(l, current, max, now))
        {
            bool again = _odd is var (c, m) && m == max && Math.Abs(c - current) <= Agree * max;
            _odd = again ? null : (current, max);
            if (!again) return null;
        }
        else _odd = null;
        _last = (current, max, now);
        return (current, max, shield);
    }

    static bool Fits((long Current, long Max, TimeSpan At) last, long current, long max, TimeSpan now) =>
        max == last.Max && current <= last.Current
        && last.Current - current <= MaxDropShare * max * Math.Max(0.5, (now - last.At).TotalSeconds);
}
