using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Platform;

/// <summary>Copies small screen regions of the latest frame off the GPU. Two implementations:
/// Desktop Duplication (Windows 10, and fallback) and borderless WGC (Windows 11).
/// Hard rule: an implementation must never start a capture that draws a border.</summary>
public interface ICaptureSource : IDisposable
{
    /// <summary>Waits up to <paramref name="timeout"/> for a new frame, then copies each region (screen pixels)
    /// into the matching image (same index; images are resized by the caller to the region size).</summary>
    /// <param name="timestamp">Frame present time as a <see cref="System.Diagnostics.Stopwatch"/> timestamp;
    /// convert with <see cref="Clock.FromTimestamp"/>.</param>
    /// <returns>False on timeout or when the output was lost (resolution change, lock screen); the caller retries.</returns>
    bool TryCapture(IReadOnlyList<PixelRect> regions, IReadOnlyList<Image> into, TimeSpan timeout, out long timestamp);
}
