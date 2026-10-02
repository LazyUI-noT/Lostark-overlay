using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>Turns debuff row and boss count reads into the debuff window's state (PLAN §4 디버프). Pure.</summary>
public static class DebuffTracker
{
    // Two missed 100 ms reads and a bit: the game's glare hides an icon for a frame.
    static readonly TimeSpan Hold = TimeSpan.FromSeconds(0.5);
    // The counts change only on a swap; a count the glare or a spell effect hides keeps its last value this long.
    static readonly TimeSpan BossHold = TimeSpan.FromSeconds(3);
    // ponytail: a count read alone with nothing to keep completes the other as 8 less it. A death breaks that (5 / 2 in
    // 18-22.mp4); every digit but 7 is read now, so drop the completion once a 7 is sampled.
    const int Raiders = 8;

    /// <summary>The state after one read of the debuff row (<paramref name="reads"/>) and of the boss's counts (null:
    /// not read this time, or not found). A hidden HUD (loading screen, cutscene) shows nothing: everything is kept as
    /// seen now, so the holds start over when it is back.</summary>
    public static DebuffStatus Update(DebuffStatus prev, IReadOnlyList<DebuffReading> reads, int? red, int? blue, TimeSpan now, bool hudVisible = true)
    {
        if (!hudVisible)
            return prev with
            {
                Blue = prev.Blue is { } b ? b with { SeenAt = now } : null, Red = prev.Red is { } r ? r with { SeenAt = now } : null,
                Lightning = prev.Lightning is { } l ? l with { SeenAt = now } : null, Swap = prev.Swap is { } w ? w with { SeenAt = now } : null,
                BossSeenAt = prev.BossRed is null ? prev.BossSeenAt : now,
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
        };
        // One sigil at a time: the one just seen replaces the other at once.
        if (reads.Any(r => r.Kind == DebuffKind.파랑) && !reads.Any(r => r.Kind == DebuffKind.빨강)) next = next with { Red = null };
        if (reads.Any(r => r.Kind == DebuffKind.빨강) && !reads.Any(r => r.Kind == DebuffKind.파랑)) next = next with { Blue = null };

        // A count missed while the other is unchanged is the glare's, so it keeps its value; else (a swap, or a 7, which
        // has no sample) it is the rest of 8.
        if (red is null && blue is not null) red = blue == prev.BossBlue && prev.BossRed is { } r0 ? r0 : Raiders - blue;
        if (blue is null && red is not null) blue = red == prev.BossRed && prev.BossBlue is { } b0 ? b0 : Raiders - red;
        if (red is not null) return next with { BossRed = red, BossBlue = blue, BossSeenAt = now };
        return now - prev.BossSeenAt <= BossHold ? next : next with { BossRed = null, BossBlue = null };
    }
}
