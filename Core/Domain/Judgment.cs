namespace GaugeOverlay.Core.Domain;

public enum JudgmentPhase { Ready, Active, Cooldown }

/// <summary>단심 is on for the active character: what to read it from, and the game's sizes that place it.</summary>
public sealed record JudgmentPlan(JudgmentSource Source, int FontSize, int BuffSize);

/// <summary>What one poll saw of 심판: its icon in the buff row, its text over the character, and the icon's timer when read
/// ("5초" = 5, "0.4초" = 0.4).</summary>
public sealed record JudgmentRead(bool Icon, bool Text, double? Seconds = null);

/// <summary>단심 (단죄/심판 rune, the player's own; user 2026-10-13): 심판 lasts <see cref="Duration"/> and is back
/// <see cref="Cycle"/> after it started. Everything follows from <see cref="TriggeredAt"/> and the time, so the clock keeps
/// running while the HUD is hidden; null = never seen (shown ready). The rest is JudgmentTracker's: whether the last poll
/// saw the icon, since when the text has been read on every poll (<see cref="TextSince"/>) and whether that run was taken
/// (<see cref="TextTaken"/>), and the icon's last timer, so only a new sighting is a trigger and a tick of the timer is seen.
/// <see cref="Previous"/>: the start of the cycle that ran out, kept for the 아이콘's ready flash after the tracker resets it.</summary>
public sealed record JudgmentState(TimeSpan? TriggeredAt, bool IconSeen = false, TimeSpan? TextSince = null, bool TextTaken = false, double? Timer = null,
    TimeSpan? Previous = null)
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(6), Cycle = TimeSpan.FromSeconds(30);
    public static readonly JudgmentState Ready = new((TimeSpan?)null);

    /// <summary>The icon or text appearing again this soon after the trigger is the same 심판: the icon stays a moment past
    /// 6 s and blinks under the game's glare; the text stays over a second.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    TimeSpan? Elapsed(TimeSpan now) => TriggeredAt is { } t && now - t < Cycle ? now - t : null;

    /// <summary>심판 can show now, so it is read: ready, or on and a second past (its icon's timer). Through the rest of the
    /// cooldown nothing of it can appear, and nothing is read (user, 2026-10-13).</summary>
    public bool Watching(TimeSpan now) => Elapsed(now) is not { } e || e < Duration + Slack;

    public JudgmentPhase Phase(TimeSpan now) =>
        Elapsed(now) is not { } e ? JudgmentPhase.Ready : e < Duration ? JudgmentPhase.Active : JudgmentPhase.Cooldown;

    /// <summary>The bar (user, 2026-10-13): while 심판 is on, the share of its 6 s left; then the share of the 24 s cooldown
    /// left, each over the whole bar. (0, 0) when ready.</summary>
    public (double Active, double Cooldown) Fill(TimeSpan now)
    {
        if (Elapsed(now) is not { } e) return (0, 0);
        return e < Duration ? ((Duration - e) / Duration, 0) : (0, (Cycle - e) / (Cycle - Duration));
    }

    public static readonly TimeSpan IconFlash = TimeSpan.FromSeconds(0.4);

    /// <summary>아이콘 (user, 2026-11-10): 심판's icon with its seconds while it is on, grey with the seconds to ready through
    /// the cooldown, and 단죄's icon while ready (what builds toward the next 심판), under a white flash for
    /// <see cref="IconFlash"/> as it comes back.</summary>
    public JudgmentIcon Icon(TimeSpan now)
    {
        if ((TriggeredAt ?? Previous) is { } t && now >= t)
        {
            var e = now - t;
            if (e < Duration) return new(false, false, false, LabelSeconds(now));
            if (e < Cycle) return new(false, true, false, LabelSeconds(now));
            if (e - Cycle < IconFlash) return new(true, false, true, null);
        }
        return new(true, false, false, null);
    }

    /// <summary>Whole seconds (rounded down, as the buff bars): 심판's while it is on, then those to ready; null when ready.</summary>
    public int? LabelSeconds(TimeSpan now) => Elapsed(now) is not { } e ? null
        : (int)(e < Duration ? Duration - e : Cycle - e).TotalSeconds;
}

/// <summary>How the 아이콘 is drawn now: 단죄's icon (ready) or 심판's, grey (the cooldown), under a white flash (just
/// ready), and the seconds on it.</summary>
public readonly record struct JudgmentIcon(bool Conviction, bool Dim, bool Flash, int? Seconds);
