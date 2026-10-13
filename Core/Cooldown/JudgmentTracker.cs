using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>Turns 심판 sightings into its cycle (<see cref="JudgmentState"/>). Pure: a new state per poll.</summary>
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
        if (prev.TriggeredAt is { } end && now - end >= JudgmentState.Cycle) prev = JudgmentState.Ready;
        if (!hudVisible) prev = prev with { IconSeen = false, TextSince = null, TextTaken = false, Timer = null };
        if (!hudVisible) return prev;
        double? timer = read.Icon && read.Seconds is { } sec and > 0 and <= 6 ? sec : null;
        TimeSpan? since = read.Text ? prev.TextSince ?? now : null;
        bool text = read.Text && prev.TextSince is not null && !prev.TextTaken, icon = read.Icon && !prev.IconSeen;
        var next = prev with { IconSeen = read.Icon, TextSince = since, TextTaken = read.Text && (prev.TextTaken || text), Timer = timer };
        // Icon and text show within a frame of each other (단죄 폰트 변경 + 심판 효과 at 100%.mp4, 48.2 s).
        if ((text || icon) && !(prev.TriggeredAt is { } t && now - t < Same)) next = next with { TriggeredAt = icon ? now : since };
        return timer is { } shown ? Timed(next, prev.Timer, shown, now) : next;
    }

    /// <summary>The icon's timer dates 심판 (user, 2026-10-13): "N초" is N to N+1 seconds left (the game rounds down), and the
    /// moment it ticks down from N+1 exactly N+1; a tenths reading is taken as it is. A cycle that disagrees with it (a missed
    /// or false sighting, the icon blinking back mid-buff) moves to it.</summary>
    static JudgmentState Timed(JudgmentState s, double? before, double shown, TimeSpan now)
    {
        bool tenths = shown < 1, tick = !tenths && before is { } b && b == shown + 1;
        double? exact = tenths ? shown : tick ? shown + 1 : null;
        double left = s.TriggeredAt is { } t ? (JudgmentState.Duration - (now - t)).TotalSeconds : double.NaN;
        bool agrees = tenths ? Math.Abs(left - shown) <= Agree : left >= shown - Agree && left <= shown + 1 + Agree;
        if (exact is null && agrees) return s;
        // "6초" (a misread, or its first frame) is at most 6 s left: never a start in the future.
        return s with { TriggeredAt = now - (JudgmentState.Duration - TimeSpan.FromSeconds(Math.Min(exact ?? shown + 0.5, JudgmentState.Duration.TotalSeconds))) };
    }

    /// <summary>A sighting this soon after the trigger is the same 심판 still on screen. A second longer than the engine reads
    /// (<see cref="JudgmentState.Watching"/>), so a poll timed across that edge can't restart the cycle.</summary>
    static readonly TimeSpan Same = JudgmentState.Duration + 2 * JudgmentState.Slack;

    /// <summary>How far (seconds) the cycle may stray from the timer before it moves: a poll's lateness and a sighting's.</summary>
    const double Agree = 0.3;
}
