using System.Diagnostics;

namespace GaugeOverlay.Core.Domain;

/// <summary>The one monotonic clock. Capture timestamps (QPC ticks from Desktop Duplication / WGC)
/// and <see cref="TimeProvider"/> timestamps are both converted here, so every time in the app is comparable.</summary>
public static class Clock
{
    public static TimeSpan FromTimestamp(long timestamp) =>
        TimeSpan.FromSeconds((double)timestamp / Stopwatch.Frequency);

    public static TimeSpan Now() => FromTimestamp(TimeProvider.System.GetTimestamp());
}
