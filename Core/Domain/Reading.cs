namespace GaugeOverlay.Core.Domain;

/// <summary>What the slot area showed in one frame (L1 vision → L0 logic).</summary>
public enum ReadKind
{
    /// <summary>HUD hidden, covered, or the crop doesn't look like the skill bar.</summary>
    Unreadable,
    /// <summary>No digits: off cooldown (whether the skill can be used right now, e.g. 마나 부족 or 침묵, isn't tracked).</summary>
    Ready,
    /// <summary>Seconds digits shown (<see cref="Reading.Value"/> = the number, e.g. 14 for "14s").</summary>
    Seconds,
    /// <summary>Minutes shown (<see cref="Reading.Value"/> = the number, e.g. 3 for "3m").</summary>
    Minutes,
    /// <summary>Digits present but not legible (partly covered).</summary>
    CooldownIllegible,
    /// <summary>A case-2 아덴 skill drawn dark: off cooldown but without enough resource (classes.json "adenDark").</summary>
    Unavailable,
}

/// <summary>How a case-2 아덴 skill shows it can't be used (classes.json "adenDark"), which differs per class:
/// <see cref="Icon"/>, its icon goes dark (발키리, 바드: true); <see cref="Label"/>, its key label turns grey and the
/// icon dims, while usable the label is white under a faint purple outline (워로드 전투태세 only: "label").</summary>
public enum DarkCue { None, Icon, Label }

/// <param name="At">Capture timestamp of the frame, on the monotonic clock (see <see cref="Clock"/>).</param>
/// <param name="Stacked">Seconds only: a stack count (a use left) is shown beside the timer, so the skill is usable
/// while it recharges.</param>
/// <param name="Gold">The game draws a gold frame around the icon (a buff lights some skills; PLAN §3 "Gold frame").</param>
/// <param name="Flash">The icon's edge is lit white: the skill was just used (chainSkill.mp4).</param>
public readonly record struct Reading(SlotId Slot, ReadKind Kind, int Value, TimeSpan At, bool Stacked = false, bool Gold = false,
    bool Flash = false);
