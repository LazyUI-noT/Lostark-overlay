namespace GaugeOverlay.Core.Domain;

/// <summary>The party buffs shown as indicators under the skill boxes (PLAN §4 버프), the same icons for every class: four read
/// from my own buff row, and 낙인 (a support's debuff on the boss, read from the boss debuff row: BrandWatch). New kinds go at the
/// end: saved records and layouts keep the numbers.</summary>
public enum BuffKind { 공증, 아덴피증, 초각스피증, 뎀증, 낙인 }

/// <summary>The order the bars are laid out and listed in 설정: 낙인 beside 공증 (user, 2026-11-15: "display together").</summary>
public static class BuffOrder
{
    public static readonly IReadOnlyList<BuffKind> Shown = [BuffKind.공증, BuffKind.낙인, BuffKind.아덴피증, BuffKind.초각스피증, BuffKind.뎀증];
}

/// <summary>One buff icon found in the buff row at <paramref name="At"/>. <paramref name="Seconds"/> is the timer
/// shown ("7초" = 7, "0.7초" = 0.7), or null when the icon is there but its timer can't be read.</summary>
public sealed record BuffReading(BuffKind Kind, double? Seconds, TimeSpan At);

/// <summary>What an indicator draws. Active with <see cref="EndsAt"/> and <see cref="Total"/> it drains; Active with no
/// end (a timer never read) it shows full. <see cref="Estimated"/>: the end is carried forward, not just read.
/// <see cref="MissedAt"/> and <see cref="Pending"/> are BuffTracker's: the first read of a run without the icon, and the end implied
/// by a read that jumped away from <see cref="EndsAt"/>, held until the next read agrees; <see cref="Resets"/>, 1 s 공증
/// resets seen in a row while the 8 s bar is held.</summary>
public sealed record BuffState(BuffKind Kind, bool Active, TimeSpan? EndsAt, TimeSpan? Total, bool Estimated, TimeSpan ChangedAt,
    TimeSpan? MissedAt = null, TimeSpan? Pending = null, int Resets = 0)
{
    public static BuffState Absent(BuffKind kind, TimeSpan at) => new(kind, false, null, null, false, at);

    public TimeSpan Remaining(TimeSpan now) =>
        EndsAt is { } end && end > now ? end - now : TimeSpan.Zero;

    /// <summary>Share left, 1 → 0, or 1 while active with no known end, 0 when absent.</summary>
    public double Progress(TimeSpan now) =>
        !Active ? 0 : EndsAt is null || Total is not { } total || total <= TimeSpan.Zero ? 1
        : Math.Clamp(Remaining(now) / total, 0, 1);
}
