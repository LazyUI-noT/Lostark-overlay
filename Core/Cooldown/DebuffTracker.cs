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
            // "N초" is N to N+1 s left: the middle is taken (every read corrects it); tenths are exact.
            TimeSpan? end = r.Seconds is { } s ? now + TimeSpan.FromSeconds(s >= 1 ? s + 0.5 : s) : was?.EndsAt;
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
