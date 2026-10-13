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
/// <see cref="Previous"/>: the start of the cycle that ran out, kept for the 준비 아이콘's flash after the tracker resets it.</summary>
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

    public static readonly TimeSpan IconFlash = TimeSpan.FromSeconds(0.4), IconFade = TimeSpan.FromSeconds(1);

    /// <summary>준비 아이콘 (user, 2026-10-13): the icon with 심판's seconds while it is on; through the cooldown nothing, or (with
    /// <paramref name="showCooldown"/>) a dim icon with the seconds to ready; at ready a white flash for <see cref="IconFlash"/>,
    /// a fade over <see cref="IconFade"/>, then nothing until the next 심판. Null: nothing to draw.</summary>
    public JudgmentIcon? Icon(TimeSpan now, bool showCooldown)
    {
        if ((TriggeredAt ?? Previous) is not { } t || now < t) return null;
        var e = now - t;
        if (e < Duration) return new(1, false, false, LabelSeconds(now));
        if (e < Cycle) return showCooldown ? new(1, true, false, LabelSeconds(now)) : null;
        var ready = e - Cycle;
        if (ready < IconFlash) return new(1, false, true, null);
        return ready < IconFlash + IconFade ? new(1 - (ready - IconFlash) / IconFade, false, false, null) : null;
    }

    /// <summary>Whole seconds (rounded down, as the buff bars): 심판's while it is on, then those to ready; null when ready.</summary>
    public int? LabelSeconds(TimeSpan now) => Elapsed(now) is not { } e ? null
        : (int)(e < Duration ? Duration - e : Cycle - e).TotalSeconds;
}

/// <summary>How the 준비 아이콘 is drawn now: its opacity, greyed (the cooldown), under a white flash (just ready), and the
/// seconds on it.</summary>
public readonly record struct JudgmentIcon(double Opacity, bool Dim, bool Flash, int? Seconds);
