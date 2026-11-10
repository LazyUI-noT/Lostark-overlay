using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>Turns 심판 sightings into its cycle (<see cref="JudgmentState"/>), and 단죄's into its 3 s. Pure: a new state per poll.</summary>
public static class JudgmentTracker
{
    /// <summary>A sighting starts the cycle: the icon not seen on the last poll, or the text read on two polls in a row (it shows
    /// for 7+ polls; one false read would start 30 s that can't be undone, as nothing is read through the cooldown), dated
    /// from its first. Not while the running 심판 can still be on screen (<see cref="Same"/>); later it restarts the cycle (the
    /// screen wins). A hidden HUD shows nothing: the clock runs on.</summary>
    public static JudgmentState Update(JudgmentState prev, JudgmentRead read, TimeSpan now, bool hudVisible)
    {
        // A cycle that has run out is ready again, and wasn't read through its cooldown: what was seen before it is gone. A hidden
        // HUD shows nothing.
        if (prev.TriggeredAt is { } end && now - end >= JudgmentState.Cycle) prev = JudgmentState.Ready with { Previous = end };
        if (!hudVisible)
            return prev with
            {
                IconSeen = false, TextSince = null, TextTaken = false, Timer = null, ConvictionSeen = false, ConvictionTimer = null, ConvictionPending = null,
            };
        double? timer = read.Icon && read.Seconds is { } sec and > 0 and <= 6 ? sec : null;
        TimeSpan? since = read.Text ? prev.TextSince ?? now : null;
        bool text = read.Text && prev.TextSince is not null && !prev.TextTaken, icon = read.Icon && !prev.IconSeen;
        var next = Conviction(prev, read, now) with
        {
            IconSeen = read.Icon, TextSince = since, TextTaken = read.Text && (prev.TextTaken || text), Timer = timer,
        };
        // Icon and text show within a frame of each other (단죄 폰트 변경 + 심판 효과 at 100%.mp4, 48.2 s).
        if ((text || icon) && !(prev.TriggeredAt is { } t && now - t < Same)) next = next with { TriggeredAt = icon ? now : since };
        if (timer is not { } shown) return next;
        var dated = Dated(next.TriggeredAt, prev.Timer, shown, now, JudgmentState.Duration);
        return dated == next.TriggeredAt ? next : next with { TriggeredAt = dated };
    }

    /// <summary>단죄 (user, 2026-11-10), dated as the buff bars are (BuffTracker): "N초" is N.9 to N s left, tenths under a
    /// second ("0.4") as they are, and the held end moves only as far as the read's window needs; a first read starts at the window's top (no
    /// more than its 3 s); a jump (a retrigger, or a misread) waits for a second read to agree. With no timer read, a new
    /// sighting starts it now and one still seen keeps it (user, 2026-11-10: restarting there flicked it back up). Its icon
    /// gone ends nothing: the clock does, or 심판 (which replaces it in the row).</summary>
    static JudgmentState Conviction(JudgmentState prev, JudgmentRead read, TimeSpan now)
    {
        double? timer = read.Conviction && PointLost(read.ConvictionSeconds) is { } sec and >= 0 and <= 3 ? sec : null;
        var s = prev with { ConvictionSeen = read.Conviction, ConvictionTimer = timer };
        // Only while 심판 is ready: a 단죄 read beside a running 심판 would restart its 3 s every poll (review, 2026-11-10).
        if (!read.Conviction || prev.Phase(now) != JudgmentPhase.Ready) return s with { ConvictionPending = null };
        var length = JudgmentState.ConvictionDuration;
        TimeSpan? end = prev.ConvictionAt is { } c && (prev.ConvictionSeen || prev.ConvictionLeft(now) is not null)
            && c + length >= now - Second ? c + length : null;
        if (timer is not { } shown) return end is null ? Convicted(s, now + length) : s with { ConvictionPending = null };
        // Still on screen is still running: at least a poll left, so a held end already past doesn't end it (review).
        var lo = now + TimeSpan.FromSeconds(Math.Max(shown, StillOn));
        var top = now + TimeSpan.FromSeconds(Math.Clamp(shown >= 1 ? shown + 1 : shown, StillOn, length.TotalSeconds));
        if (end is not { } e) return Convicted(s, top);
        // Tenths above the held end by more than two tenths wait for a second read to agree, as BuffTracker's: a bright floor
        // turns "0.4" into "0.9" for a frame. Not past a held end: waiting there would blink 단죄 out for a poll (review).
        if (shown < 1 && e > now && lo - e > TenthsSlack && !(prev.ConvictionPending is { } q && (q - lo).Duration() <= TenthsSlack))
            return s with { ConvictionPending = lo };
        if (e >= lo - Second && e <= top + Second) return Convicted(s, e < lo ? lo : e > top ? top : e);
        return prev.ConvictionPending is { } p && (p - top).Duration() <= Second ? Convicted(s, top) : s with { ConvictionPending = top };
    }

    static JudgmentState Convicted(JudgmentState s, TimeSpan end) =>
        s with { ConvictionAt = end - JudgmentState.ConvictionDuration, ConvictionPending = null };

    static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    static readonly TimeSpan TenthsSlack = TimeSpan.FromSeconds(0.2);
    const double StillOn = 0.05;

    /// <summary>A whole read 4–9 can't be 단죄's (3 s at most): it is tenths whose "0." the reader lost, as BuffTracker's.</summary>
    static double? PointLost(double? shown) =>
        shown is { } v && v > JudgmentState.ConvictionDuration.TotalSeconds && v < 10 && v == Math.Floor(v) ? v / 10 : shown;

    /// <summary>A buff timer dates its start (user, 2026-10-13): "N초" is N to N+1 seconds left (the game rounds down), and the
    /// moment it ticks down from N+1 exactly N+1; a tenths reading is taken as it is. A start that disagrees with it (a missed
    /// or false sighting, the icon blinking back mid-buff, a retrigger) moves to it; never into the future.</summary>
    static TimeSpan? Dated(TimeSpan? start, double? before, double shown, TimeSpan now, TimeSpan length)
    {
        bool tenths = shown < 1, tick = !tenths && before is { } b && b == shown + 1;
        double? exact = tenths ? shown : tick ? shown + 1 : null;
        double left = start is { } t ? (length - (now - t)).TotalSeconds : double.NaN;
        bool agrees = tenths ? Math.Abs(left - shown) <= Agree : left >= shown - Agree && left <= shown + 1 + Agree;
        if (exact is null && agrees) return start;
        return now - (length - TimeSpan.FromSeconds(Math.Min(exact ?? shown + 0.5, length.TotalSeconds)));
    }

    /// <summary>A sighting this soon after the trigger is the same 심판 still on screen. A second longer than the engine reads
    /// (<see cref="JudgmentState.Watching"/>), so a poll timed across that edge can't restart the cycle.</summary>
    static readonly TimeSpan Same = JudgmentState.Duration + 2 * JudgmentState.Slack;

    /// <summary>How far (seconds) the start may stray from the timer before it moves: a poll's lateness and a sighting's.</summary>
    const double Agree = 0.3;
}
