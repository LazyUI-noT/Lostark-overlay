namespace GaugeOverlay.Core.Domain;

public enum JudgmentPhase { Ready, Active, Cooldown }

/// <summary>단심 is on for the active character: what to read it from, and the game's sizes that place it.</summary>
public sealed record JudgmentPlan(JudgmentSource Source, int FontSize, int BuffSize);

/// <summary>What one poll saw of 심판: its icon in the buff row, its text over the character, and the icon's timer when read
/// ("5초" = 5, "0.4초" = 0.4); and 단죄's icon with its timer.</summary>
public sealed record JudgmentRead(bool Icon, bool Text, double? Seconds = null, bool Conviction = false, double? ConvictionSeconds = null);

/// <summary>단심 (단죄/심판 rune, the player's own; user 2026-10-13): 심판 lasts <see cref="Duration"/> and is back
/// <see cref="Cycle"/> after it started. Everything follows from <see cref="TriggeredAt"/> and the time, so the clock keeps
/// running while the HUD is hidden; null = never seen (shown ready). The rest is JudgmentTracker's: whether the last poll
/// saw the icon, since when the text has been read on every poll (<see cref="TextSince"/>) and whether that run was taken
/// (<see cref="TextTaken"/>), and the icon's last timer, so only a new sighting is a trigger and a tick of the timer is seen.
/// <see cref="Previous"/>: the start of the cycle that ran out, kept for the 아이콘's ready flash after the tracker resets it.
/// <see cref="ConvictionAt"/>: when 단죄 last went off (user, 2026-11-10): it runs <see cref="ConvictionDuration"/>, shown
/// while 심판 is ready (a running 심판 or its cooldown wins); <see cref="ConvictionSeen"/> and <see cref="ConvictionTimer"/>
/// are the last poll's sighting of its icon and timer, as for 심판; <see cref="ConvictionPending"/> an end one read jumped to,
/// waiting for a second read to agree.</summary>
public sealed record JudgmentState(TimeSpan? TriggeredAt, bool IconSeen = false, TimeSpan? TextSince = null, bool TextTaken = false, double? Timer = null,
    TimeSpan? Previous = null, TimeSpan? ConvictionAt = null, bool ConvictionSeen = false, double? ConvictionTimer = null,
    TimeSpan? ConvictionPending = null)
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(6), Cycle = TimeSpan.FromSeconds(30),
        ConvictionDuration = TimeSpan.FromSeconds(3);

    /// <summary>Seconds of 단죄 left, while 심판 is ready; null otherwise.</summary>
    public double? ConvictionLeft(TimeSpan now) =>
        Elapsed(now) is null && ConvictionAt is { } c && now >= c && now - c < ConvictionDuration
            ? (ConvictionDuration - (now - c)).TotalSeconds : null;
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
    /// the cooldown, 단죄's icon with its seconds while it runs, under a white flash for <see cref="IconFlash"/> as 심판
    /// comes back; null (nothing drawn) while ready with no 단죄 running.</summary>
    public JudgmentIcon? Icon(TimeSpan now)
    {
        if ((TriggeredAt ?? Previous) is { } t && now >= t)
        {
            var e = now - t;
            if (e < Duration) return new(false, false, false, LabelSeconds(now));
            if (e < Cycle) return new(false, true, false, LabelSeconds(now));
            if (e - Cycle < IconFlash) return new(true, false, true, LabelSeconds(now));
        }
        return ConvictionLeft(now) is null ? null : new(true, false, false, LabelSeconds(now));
    }

    /// <summary>The seconds shown, rounded down as the game does (user, 2026-11-10): whole from 1 s up ("2" from 2.9 to 2 s
    /// left), tenths under it (0.9 … 0.0): 심판's while it is on, then those to ready; while ready, 단죄's
    /// (<see cref="ConvictionLeft"/>), else null. An exact step shows the one below (3.0 → 2, 1.0 → 0.9), so the moment a start
    /// is set never shows a number the game doesn't.</summary>
    public double? LabelSeconds(TimeSpan now) => Elapsed(now) is not { } e ? ConvictionLeft(now) is { } c ? Down(c) : null
        : Down((e < Duration ? Duration - e : Cycle - e).TotalSeconds);

    static double Down(double seconds) => seconds > 1 ? Math.Ceiling(seconds) - 1
        : Math.Max(0, Math.Ceiling(Math.Round(seconds * 10, 6)) - 1) / 10;

    /// <summary>A label as drawn: "2", or "0.4" under a second.</summary>
    public static string LabelText(double seconds) =>
        seconds >= 1 ? seconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>How the 아이콘 is drawn now: 단죄's icon (ready) or 심판's, grey (the cooldown), under a white flash (just
/// ready), and the seconds on it.</summary>
public readonly record struct JudgmentIcon(bool Conviction, bool Dim, bool Flash, double? Seconds);
