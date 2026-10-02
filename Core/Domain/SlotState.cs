namespace GaugeOverlay.Core.Domain;

public enum SlotPhase
{
    /// <summary>Never read and nothing recorded: hatched on the overlay.</summary>
    Unknown,
    Ready,
    /// <summary>Seconds cooldown: sweep runs from <see cref="SlotState.Total"/> to <see cref="SlotState.EndsAt"/>.</summary>
    Cooldown,
    /// <summary>Game shows minutes: dimmed, no sweep.</summary>
    LongCooldown,
    /// <summary>A case-2 아덴 skill without enough resource (drawn dark in game): dimmed, no timer. Sent to the party as
    /// a LongCooldown of 0 minutes, which version 8 peers already draw the same way.</summary>
    Unavailable,
}

/// <summary>The tracked state of one slot (L0 logic → L3/L4). Immutable; the tracker returns a new one per update.</summary>
/// <param name="EndsAt">Predicted ready time on the monotonic clock (Cooldown; also kept on a Ready read straight out of
/// a cooldown, see CooldownTracker.ResumeWindow).</param>
/// <param name="Total">Full length the sweep treats as 100% (Cooldown only).</param>
/// <param name="Estimated">True while counting on the recorded time without a confirming read.</param>
/// <param name="ChangedAt">When <see cref="Phase"/> last changed; the renderer uses it for the ready shine.</param>
/// <param name="Minutes">Minutes shown while LongCooldown.</param>
/// <param name="Pending">A seconds reading more than a second off <see cref="EndsAt"/>, held (with the EndsAt it implies)
/// until the next digit agrees with it (CooldownTracker). Local only: not sent to the party.</param>
/// <param name="Stacked">Cooldown of a stack skill with a use left (<see cref="Reading.Stacked"/>): drawn bright with
/// its recharge sweep, here and for the party.</param>
/// <param name="CastSeen">False for a LongCooldown first seen midway (app start): its cast time is unknown, so the
/// seconds that follow get no sweep. Local only.</param>
/// <param name="Gold">The icon has the game's gold frame now (<see cref="Reading.Gold"/>): drawn as a border, here and
/// for the party.</param>
/// <param name="UsedAt">Cooldown entered on a use flash before the game shows a number (a chain skill's next part is
/// still pressable): the last flash. Local only.</param>
/// <param name="StackSkill">The slot has shown a stack count since it was last unreadable. The count is gone at 0 uses,
/// so this is what tells a stack skill with no use left (drawn grey) from an ordinary cooldown. Sent to the party.</param>
public sealed record SlotState(
    SlotId Slot,
    SlotPhase Phase,
    TimeSpan? EndsAt,
    TimeSpan? Total,
    bool Estimated,
    TimeSpan ChangedAt,
    int Minutes = 0,
    (int Value, TimeSpan EndsAt)? Pending = null,
    bool Stacked = false,
    bool CastSeen = true,
    bool Gold = false,
    bool StackSkill = false,
    TimeSpan? UsedAt = null)
{
    public static SlotState Unknown(SlotId slot) => new(slot, SlotPhase.Unknown, null, null, false, TimeSpan.Zero);

    /// <summary>Remaining time at <paramref name="now"/>, clamped at zero; null unless Cooldown.</summary>
    public TimeSpan? Remaining(TimeSpan now) =>
        Phase == SlotPhase.Cooldown && EndsAt is { } end ? (end > now ? end - now : TimeSpan.Zero) : null;

    /// <summary>Sweep fill 0..1 (elapsed share of <see cref="Total"/>); null unless Cooldown.</summary>
    public double? Progress(TimeSpan now) =>
        Remaining(now) is { } rem && Total is { } total && total > TimeSpan.Zero
            ? Math.Clamp(1 - rem / total, 0, 1)
            : null;
}
