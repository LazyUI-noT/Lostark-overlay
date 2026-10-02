namespace GaugeOverlay.Core.Domain;

/// <summary>The four party buffs shown as indicators under the skill boxes (PLAN §4 버프). The icons are the same for
/// every class.</summary>
public enum BuffKind { 공증, 아덴피증, 초각스피증, 뎀증 }

/// <summary>One buff icon found in the buff row at <paramref name="At"/>. <paramref name="Seconds"/> is the timer
/// shown ("7초" = 7, "0.7초" = 0.7), or null when the icon is there but its timer can't be read.</summary>
public sealed record BuffReading(BuffKind Kind, double? Seconds, TimeSpan At);

/// <summary>What an indicator draws. Active with <see cref="EndsAt"/> and <see cref="Total"/> it drains; Active with no
/// end (a timer never read) it shows full. <see cref="Estimated"/>: the end is carried forward, not just read.
/// <see cref="Misses"/> and <see cref="Pending"/> are BuffTracker's: reads in a row without the icon, and the end implied
/// by a read that jumped away from <see cref="EndsAt"/>, held until the next read agrees; <see cref="Resets"/>, 1 s 공증
/// resets seen in a row while the 8 s bar is held.</summary>
public sealed record BuffState(BuffKind Kind, bool Active, TimeSpan? EndsAt, TimeSpan? Total, bool Estimated, TimeSpan ChangedAt,
    int Misses = 0, TimeSpan? Pending = null, int Resets = 0)
{
    public static BuffState Absent(BuffKind kind, TimeSpan at) => new(kind, false, null, null, false, at);

    public TimeSpan Remaining(TimeSpan now) =>
        EndsAt is { } end && end > now ? end - now : TimeSpan.Zero;

    /// <summary>Share left, 1 → 0, or 1 while active with no known end, 0 when absent.</summary>
    public double Progress(TimeSpan now) =>
        !Active ? 0 : EndsAt is null || Total is not { } total || total <= TimeSpan.Zero ? 1
        : Math.Clamp(Remaining(now) / total, 0, 1);
}
