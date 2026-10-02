using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>Turns buff row reads into one indicator's state (PLAN §4 버프). Pure: a new state per read.</summary>
public static class BuffTracker
{
    static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    const int MissesToAbsent = 2;
    // An icon back this soon after going absent (the glare hid it) carries on where it was, keeping its Total.
    static readonly TimeSpan ResumeWindow = TimeSpan.FromSeconds(1);
    // Every buff lasts 20 s or less, so a tens digit the reader missed (BuffReader.MinTens) is 1 or 2.
    const double LongestBuff = 20;
    // Half a second: well above the 0.1 s steps of a decimal timer, well below a 1 s refresh.
    static readonly TimeSpan RefreshGap = TimeSpan.FromSeconds(0.5);
    // Two tenths: a tenths read above the held end by more is a blip until confirmed.
    static readonly TimeSpan TenthsSlack = TimeSpan.FromSeconds(0.2);
    // 공증: a read of 2 s or more is the 8 s one (the 1 s one never shows more than 1); two resets in a row make the bar 1 s.
    const double LongFrom = 2;
    const int ResetsToShort = 2;

    // Full durations by the caster's class (from the user). The icon doesn't tell the caster, so Total is the smallest
    // that fits, or the longest seen when none does.
    static readonly Dictionary<BuffKind, double[]> Durations = new()
    {
        [BuffKind.공증] = [1, 8],             // 8 for every support; 바드 and 도화가 also have a 1 s one, recast 8 times
        [BuffKind.아덴피증] = [12, 16, 20],   // 바드 12/16, 도화가 12 (stacks), 발키리 12, 홀리나이트 16–20
        [BuffKind.초각스피증] = [10, 12, 15], // 발키리 and 홀리나이트 10, 도화가 12, 바드 15
        [BuffKind.뎀증] = [20],               // differs per support class (to come); 20 s observed on 발키리 (first read at 19)
    };

    /// <summary>The state of <paramref name="prev"/>'s buff after a read of the whole row. <paramref name="readings"/>
    /// may hold other buffs and, for 공증, two icons (two supports): the longer one counts. "N초" means the remaining
    /// time is in [N, N+1): the game rounds down, so "1초" is 1.9 to 1 s left. A decimal is taken as-is.</summary>
    public static BuffState Update(BuffState prev, IReadOnlyList<BuffReading> readings, TimeSpan now, bool hudVisible)
    {
        // A hidden HUD shows nothing: freeze, and let the count start over when it's back.
        if (!hudVisible) return prev.Active ? prev with { Estimated = true, Misses = 0 } : prev;

        var mine = readings.Where(r => r.Kind == prev.Kind).Select(r => r with { Seconds = PointLost(r.Kind, r.Seconds) }).ToList();
        if (mine.Count == 0)
            return !prev.Active ? prev
                : prev.Misses + 1 >= MissesToAbsent ? prev with { Active = false, Estimated = false, ChangedAt = now, Misses = 0, Pending = null }
                : prev with { Misses = prev.Misses + 1, Estimated = true };

        var seen = prev.Active ? prev with { Misses = 0 }
            : prev.EndsAt is not null && now - prev.ChangedAt <= ResumeWindow ? prev with { Active = true }
            : new BuffState(prev.Kind, true, null, null, false, now);
        double? longest = mine.Max(r => r.Seconds);
        // 공증 icons beside a held 8 s bar that all show under 2 s are the 1 s AoE's (1sdoublebuff.mp4): the bar follows its
        // own icon to 0, and once that one is gone the AoE's is a reset, taken at once as the 1 s bar.
        if (OwnRead(seen, mine, now) is { } own) (longest, seen) = (own, seen with { Resets = ResetsToShort - 1 });
        if (longest is not { } shown) return seen with { Estimated = true };
        if (seen.EndsAt is not { } end) return Start(seen, FirstRead(seen.Kind, shown), now);

        double seconds = RestoreTens(shown, (end - now).TotalSeconds);
        var (lo, hi) = Window(seconds, now);
        // A 공증 timer showing clearly more than was held is a reset (the 1 s 공증's AoE; the 8 s one shares its icon). A
        // held 8 s bar keeps its length through the first reset, so it drains to 0 without refilling; the second reset in
        // a row is standing on the AoE, and the bar becomes 1 s long. Other buffs have no 1 s version: a misread there.
        bool refreshed = seen.Kind == BuffKind.공증 && lo - end > RefreshGap;
        bool holdLong = refreshed && seconds < LongFrom && seen.Total > Second && seen.Resets + 1 < ResetsToShort;
        // Tenths reading higher than held, short of a reset, wait for a second read to agree: a bright floor turns "0.3"
        // into "0.8" for one frame (도화가support (2).mp4 at 31.2 s).
        if (!refreshed && seconds < 1 && lo - end > TenthsSlack && !(seen.Pending is { } p && (p - lo).Duration() <= TenthsSlack))
            return seen with { Pending = lo, Estimated = true };
        if (end >= lo - Second && end <= hi + Second)
            return seen with
            {
                EndsAt = end < lo ? lo : end > hi ? hi : end,
                Total = holdLong ? seen.Total : Fit(seen.Kind, seconds, refreshed ? null : seen.Total), Estimated = false, Pending = null,
                Resets = holdLong ? seen.Resets + 1 : refreshed ? 0 : seen.Resets,
            };

        // A jump. With the other 공증 unreadable, a shorter read is that one, not this: keep the longer estimate.
        if (hi < end && mine.Any(r => r.Seconds is null)) return seen with { Estimated = true, Pending = null };

        // Glare and look-alike digits make one-read blips (BuffSizeTests.KnownWrong), so a second read must agree, its
        // tens restored against the held end. A jump up is most likely a recast: it is held at the value a fresh cast fits.
        if (seen.Pending is { } pending)
        {
            double again = RestoreTens(shown, (pending - now).TotalSeconds);
            var agreed = now + TimeSpan.FromSeconds(again);
            if ((pending - agreed).Duration() <= Second)
                return agreed > end
                    ? Start(seen with { ChangedAt = now, Total = null }, again, now)   // recast
                    : seen with { EndsAt = agreed, Estimated = false, Pending = null };
        }
        return seen with { Pending = now + TimeSpan.FromSeconds(hi > end ? FirstRead(seen.Kind, shown) : seconds), Estimated = true };
    }

    /// <summary>A (re)cast: ends at the top of the read's window, but no later than its full length allows.</summary>
    static BuffState Start(BuffState s, double seconds, TimeSpan now)
    {
        var total = Fit(s.Kind, seconds, s.Total);
        var end = Window(seconds, now).Hi;
        return s with { EndsAt = end < now + total ? end : now + total, Total = total, Estimated = false, Pending = null, Resets = 0 };
    }

    /// <summary>Of two or more 공증 reads while an 8 s bar is held: the one nearest the held end, when every other one
    /// shows under <see cref="LongFrom"/> (the 1 s AoE); else null, and the longest counts (a second support's 8 s).</summary>
    static double? OwnRead(BuffState s, List<BuffReading> mine, TimeSpan now)
    {
        if (s.Kind != BuffKind.공증 || !(s.Total > Second) || s.EndsAt is not { } held) return null;
        var reads = mine.Select(r => r.Seconds).OfType<double>().ToList();
        if (reads.Count < 2) return null;
        double Gap(double v) => Window(v, now) is var (lo, hi) && held < lo ? (lo - held).TotalSeconds
            : held > hi ? (held - hi).TotalSeconds : 0;
        int own = reads.IndexOf(reads.MinBy(Gap));
        return reads.Where((v, i) => i != own).All(v => v < LongFrom) ? reads[own] : null;
    }

    /// <summary>A whole read past the buff's longest duration can only be tenths whose "0." the reader lost on a bright
    /// floor (공증: "9" for "0.9"; 공증bug.mp4).</summary>
    static double? PointLost(BuffKind kind, double? shown) =>
        shown is { } v && Whole(v) && v > Durations[kind].Max() ? v / 10 : shown;

    /// <summary>The ends a timer showing <paramref name="seconds"/> at <paramref name="now"/> allows.</summary>
    static (TimeSpan Lo, TimeSpan Hi) Window(double seconds, TimeSpan now)
    {
        var lo = now + TimeSpan.FromSeconds(seconds);
        return (lo, seconds < 1 ? lo : lo + Second);   // whole seconds round down
    }

    /// <summary>The first read of a (re)cast buff, a missed tens restored as the value nearest one of the buff's full
    /// durations (a buff is first read just after it is cast). Values above its longest duration are left out.</summary>
    static double FirstRead(BuffKind kind, double shown)
    {
        var known = Durations[kind];
        if (!Whole(shown)) return shown;
        return Tens(shown).Where(c => c <= known.Max()).DefaultIfEmpty(shown).MinBy(c => known.Min(d => Math.Abs(d - c)));
    }

    /// <summary>A whole-second read below 10 may have lost its tens: the candidate whose window lies closest to the
    /// <paramref name="predicted"/> remaining time.</summary>
    static double RestoreTens(double shown, double predicted) =>
        !Whole(shown) ? shown
        : Tens(shown).MinBy(c => predicted < c ? c - predicted : predicted > c + 1 ? predicted - c - 1 : 0);

    /// <summary>A whole-second read that may have lost its tens: 1–9, or 0 ("10" and "20" without it; the game's own
    /// "0.0" reads the same).</summary>
    static bool Whole(double shown) => shown < 10 && (shown == 0 || shown >= 1);

    static IEnumerable<double> Tens(double shown) => new[] { shown, shown + 10, shown + 20 }.Where(c => c <= LongestBuff);

    static TimeSpan Fit(BuffKind kind, double seconds, TimeSpan? total)
    {
        double longest = Math.Max(seconds, total?.TotalSeconds ?? 0);
        return TimeSpan.FromSeconds(Durations[kind].Where(d => d >= longest).DefaultIfEmpty(longest).Min());
    }
}
