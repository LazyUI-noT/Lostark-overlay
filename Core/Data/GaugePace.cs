namespace GaugeOverlay.Core.Data;

/// <summary>How often the 아덴 게이지 is read: every 100 ms, and every frame (16 ms, 60 fps) for 0.1 s after a skill key
/// press, for every class, so a change after the press shows sooner.</summary>
public static class GaugePace
{
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(100), Fast = TimeSpan.FromMilliseconds(16),
        FastFor = TimeSpan.FromMilliseconds(100);

    /// <summary>The wait between reads at <paramref name="now"/>, the last skill key having been pressed at
    /// <paramref name="pressedAt"/> (null = none yet).</summary>
    public static TimeSpan Every(TimeSpan now, TimeSpan? pressedAt) =>
        pressedAt is { } p && now - p < FastFor ? Fast : Slow;
}
