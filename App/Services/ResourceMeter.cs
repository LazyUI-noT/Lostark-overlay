using System.Diagnostics;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Services;

/// <summary>What 리소스 검사 shows now: CPU by box, the app's GPU (null: 측정할 수 없음) and the frame gaps.</summary>
public sealed record ResourceSample(ResourceReport Report, double? Gpu, FrameGapSummary Gaps, double Seconds);

/// <summary>리소스 검사: on while the 표시 page shows the check. Reads Perf's running totals, the process CPU and the GPU counter once
/// per <see cref="Sample"/>, for the time since <see cref="Start"/>. Kept on this PC: nothing is sent or written. UI thread.</summary>
public sealed class ResourceMeter : IDisposable
{
    readonly Action<FrameGaps?> _watchFrames;
    readonly GpuCounter? _gpu = GpuCounter.Open();
    readonly FrameGaps _gaps = new();
    readonly long _started = Stopwatch.GetTimestamp();
    readonly TimeSpan _cpuStarted = Process.GetCurrentProcess().TotalProcessorTime;
    double? _lastGpu;

    /// <param name="watchFrames">Hands the engine the frame-gap tracker (null: stop).</param>
    public ResourceMeter(Action<FrameGaps?> watchFrames)
    {
        _watchFrames = watchFrames;
        Perf.Watch(true);
        _watchFrames(_gaps);
    }

    public ResourceSample Sample()
    {
        double seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        double cpu = (Process.GetCurrentProcess().TotalProcessorTime - _cpuStarted).TotalSeconds;
        if (_gpu is not null) _lastGpu = _gpu.Read() ?? _lastGpu;
        return new(ResourceCheck.Build(Perf.Seconds(), cpu, seconds, Environment.ProcessorCount), _gpu is null ? null : _lastGpu, _gaps.Summary(), seconds);
    }

    public void Dispose()
    {
        _watchFrames(null);
        Perf.Watch(false);
        _gpu?.Dispose();
    }
}
