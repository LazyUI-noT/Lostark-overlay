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
/// (<see cref="TextTaken"/>), and the icon's last timer, so only a new sighting is a trigger and a tick of the timer is seen.</summary>
public sealed record JudgmentState(TimeSpan? TriggeredAt, bool IconSeen = false, TimeSpan? TextSince = null, bool TextTaken = false, double? Timer = null)
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

    /// <summary>Shares of the 30 s bar still left: the 심판 part (drains first), then the cooldown part; (0, 0) when ready.</summary>
    public (double Active, double Cooldown) Fill(TimeSpan now)
    {
        if (Elapsed(now) is not { } e) return (0, 0);
        double left = (Cycle - e) / Cycle, wait = (Cycle - Duration) / Cycle;
        return (Math.Max(0, left - wait), Math.Min(left, wait));
    }

    /// <summary>Whole seconds (rounded down, as the buff bars): 심판's while it is on, then those to ready; null when ready.</summary>
    public int? LabelSeconds(TimeSpan now) => Elapsed(now) is not { } e ? null
        : (int)(e < Duration ? Duration - e : Cycle - e).TotalSeconds;
}
