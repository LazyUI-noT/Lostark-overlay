using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>Turns debuff row and boss count reads into the debuff window's state (PLAN §4 디버프). Pure.</summary>
public static class DebuffTracker
{
    // Two missed 100 ms reads and a bit: the game's glare hides an icon for a frame.
    static readonly TimeSpan Hold = TimeSpan.FromSeconds(0.5);
    // The counts change only on a swap; a count the glare or a spell effect hides keeps its last value this long.
    static readonly TimeSpan BossHold = TimeSpan.FromSeconds(3);

    /// <summary>The state after one read of the debuff row (<paramref name="reads"/>) and of the boss's counts (null:
    /// not read this time, or not found). A hidden HUD (loading screen, cutscene) shows nothing: everything is kept as
    /// seen now, so the holds start over when it is back. The 결투자 cycle is the exception: it runs on the real clock.</summary>
    public static DebuffStatus Update(DebuffStatus prev, IReadOnlyList<DebuffReading> reads, int? red, int? blue, TimeSpan now, bool hudVisible = true)
    {
        if (!hudVisible)
            return prev with
            {
                Blue = prev.Blue is { } b ? b with { SeenAt = now } : null, Red = prev.Red is { } r ? r with { SeenAt = now } : null,
                Lightning = prev.Lightning is { } l ? l with { SeenAt = now } : null, Swap = prev.Swap is { } w ? w with { SeenAt = now } : null,
                BossSeenAt = prev.BossRed is null ? prev.BossSeenAt : now,
                Duelist = Duel(prev.Duelist, false, false, now),
            };

        DebuffMark? Mark(DebuffKind kind, DebuffMark? was)
        {
            if (reads.FirstOrDefault(r => r.Kind == kind) is not { } r)
                return was is not null && now - was.SeenAt <= Hold ? was : null;
            // "N초" is N to N+1 s left: the first read takes the middle, later ones keep the end and move it only as far as
            // their own range needs (at 5–10 reads a second, resetting to the middle each time made the bar jitter); tenths are exact.
            TimeSpan? end = r.Seconds is not { } s ? was?.EndsAt
                : s < 1 ? now + TimeSpan.FromSeconds(s)
                : Clamp(was?.EndsAt ?? now + TimeSpan.FromSeconds(s + 0.5), now + TimeSpan.FromSeconds(s), now + TimeSpan.FromSeconds(s + 1));
            return new DebuffMark(now, end);
        }

        var next = prev with
        {
            Blue = Mark(DebuffKind.파랑, prev.Blue), Red = Mark(DebuffKind.빨강, prev.Red),
            Lightning = Mark(DebuffKind.번개, prev.Lightning), Swap = Mark(DebuffKind.교체, prev.Swap),
            Duelist = Duel(prev.Duelist, reads.Any(r => r.Kind == DebuffKind.결투자), reads.Any(r => r.Kind is DebuffKind.파랑 or DebuffKind.빨강), now),
        };
        // One sigil at a time: the one just seen replaces the other at once.
        if (reads.Any(r => r.Kind == DebuffKind.파랑) && !reads.Any(r => r.Kind == DebuffKind.빨강)) next = next with { Red = null };
        if (reads.Any(r => r.Kind == DebuffKind.빨강) && !reads.Any(r => r.Kind == DebuffKind.파랑)) next = next with { Blue = null };

        // A count missed while the other is unchanged is the glare's, so it keeps its value. Otherwise (nothing to keep, a
        // swap, or a digit with no sample) it is unknown, and the pair is unsure: the swap box shows "?" rather than a guess
        // (8 less the one read broke after a death, 5 / 2 in 18-22.mp4).
        if (red is null && blue is not null && blue == prev.BossBlue) red = prev.BossRed;
        if (blue is null && red is not null && red == prev.BossRed) blue = prev.BossBlue;
        if (red is not null && blue is not null) return next with { BossRed = red, BossBlue = blue, BossSeenAt = now };
        if (red is not null || blue is not null) return next with { BossRed = null, BossBlue = null };
        return now - prev.BossSeenAt <= BossHold ? next : next with { BossRed = null, BossBlue = null };
    }

    static TimeSpan Clamp(TimeSpan v, TimeSpan min, TimeSpan max) => v < min ? min : v > max ? max : v;

    /// <summary>A read of the 결투자's timer ("38초"; null: not read). It ticks every whole second from when the debuff came, so a
    /// tick (one less than the last read) seen at <paramref name="now"/> puts that instant whole seconds back: the latest one at or
    /// before the first sighting, under a second before it (the icon is read 3–5 times a second). Anything else only keeps the
    /// value. Pure.</summary>
    public static DebuffStatus DuelTimer(DebuffStatus s, double? seconds, TimeSpan now)
    {
        if (s.Duelist is not { } d || seconds is not { } v || v < 1) return s;
        int n = (int)Math.Round(v);
        var next = d with { Timer = n };
        if (d.Timer is { } prev && n == prev - 1 && now > d.FirstSeenAt)
        {
            var start = now - TimeSpan.FromSeconds(Math.Ceiling((now - d.FirstSeenAt).TotalSeconds));
            if (start <= d.FirstSeenAt && d.FirstSeenAt - start < TimeSpan.FromSeconds(1)) next = next with { FirstSeenAt = start };
        }
        return s with { Duelist = next };
    }

    /// <summary>The 결투자 cycle (user, 2026-10-13): a sigil read without 결투자 for <see cref="DuelMark.Gone"/> means the
    /// debuff is over, so it is removed and the next sighting counts down again. Both gone, or a hidden HUD, is most likely
    /// a cutscene: the mark is kept, so the icon coming back is the same cycle (no second countdown), until
    /// <see cref="DuelMark.Keep"/> after its first sighting. <paramref name="sigil"/>: a 파랑 or 빨강 sigil in this read.</summary>
    static DuelMark? Duel(DuelMark? was, bool seen, bool sigil, TimeSpan now)
    {
        if (was is null || now - was.FirstSeenAt >= DuelMark.Keep) return seen ? new DuelMark(now) : null;
        var kept = was with { Ended = !was.IsActive(now) };
        if (seen) return kept with { GoneSince = null };
        if (!sigil) return kept with { GoneSince = null };   // the half second is one unbroken run of sigil-only reads
        var since = was.GoneSince ?? now;
        return now - since >= DuelMark.Gone ? null : kept with { GoneSince = since };
    }
}
