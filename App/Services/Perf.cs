using System.Diagnostics;
using System.Runtime.InteropServices;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Services;

/// <summary>Where the app's own time goes: each part adds the time it took, and once a minute the log gets one "[perf]" line,
/// per part in ms per second (10 ms/s = 1% of one core). Any thread. WPF's own render thread (turning drawings into pixels)
/// and the runtime aren't counted: they are the process's CPU minus these. Times are wall clock (a part that waits counts
/// its wait); a part added with <see cref="AddCpu"/> also shows "[CPU]", the thread's own CPU time over it (Windows charges
/// it per scheduler tick, so it is right over a minute, not per call). <see cref="Count"/> adds events, shown per second.</summary>
public static partial class Perf
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(60);
    static readonly object Gate = new();
    static readonly Dictionary<string, long> Ticks = [];
    static readonly Dictionary<string, long> Counts = [];
    static long _since = Stopwatch.GetTimestamp();

    /// <summary>Times <paramref name="work"/> on this thread: wall time as before, and since 리소스 검사 also the thread's CPU time
    /// (the " [CPU]" entry, not in the total). Only for work that runs on the calling thread.</summary>
    public static T Run<T>(string part, Func<T> work) => RunCpu(part, work);

    public static void Run(string part, Action work) => Run(part, () => { work(); return 0; });

    /// <summary><see cref="Run{T}"/> with the thread's own CPU time too (<see cref="AddCpu"/>): the wall time alone counts
    /// waits for other programs (a video player in a replay, a slower core after idling).</summary>
    public static T RunCpu<T>(string part, Func<T> work)
    {
        long t0 = Stopwatch.GetTimestamp(), cpu0 = ThreadCpu();
        try { return work(); }
        finally { AddCpu(part, Stopwatch.GetTimestamp() - t0, ThreadCpu() - cpu0); }
    }

    public static void Add(string part, long ticks)
    {
        string? line = null;
        lock (Gate)
        {
            if (_watch is not null)
            {
                _watch[part] = _watch.GetValueOrDefault(part) + ticks;
                if (!part.EndsWith(CpuTag)) _sinceFrame[part] = _sinceFrame.GetValueOrDefault(part) + ticks;
            }
            Ticks[part] = Ticks.GetValueOrDefault(part) + ticks;
            long now = Stopwatch.GetTimestamp();
            double seconds = (now - _since) / (double)Stopwatch.Frequency;
            if (seconds >= Every.TotalSeconds)
            {
                double total = Ticks.Values.Sum() * 1000.0 / Stopwatch.Frequency / seconds;
                total -= Ticks.Where(kv => kv.Key.EndsWith(CpuTag)).Sum(kv => kv.Value) * 1000.0 / Stopwatch.Frequency / seconds;
                line = $"[perf] {total:0.0} ms/s in all: " + string.Join(", ", Ticks.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} {kv.Value * 1000.0 / Stopwatch.Frequency / seconds:0.0}"));
                if (Counts.Count > 0) line += " | per s: " + string.Join(", ", Counts.Select(kv => $"{kv.Key} {kv.Value / seconds:0.0}"));
                if (Delays.Count > 0) line += " | 지연 ms (중간/95%/최대): " + string.Join(", ", Delays.Select(kv => $"{kv.Key} {DelayStats.Text(kv.Value)}"));
                line += " | " + Whole(seconds, Ticks.Where(kv => kv.Key.EndsWith(CpuTag)).Sum(kv => kv.Value) * 1000.0 / Stopwatch.Frequency / seconds);
                Ticks.Clear();
                Counts.Clear();
                Delays.Clear();
                _since = now;
            }
        }
        if (line is not null) Trace.WriteLine(line);
    }

    // The whole process since the last line (under Gate): its CPU and the runtime's garbage collection.
    static TimeSpan _cpuWas = CpuNow(), _gcPauseWas = GC.GetTotalPauseDuration();
    static long _allocatedWas = GC.GetTotalAllocatedBytes();
    static readonly int[] _collectionsWas = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];

    static TimeSpan CpuNow() { using var p = System.Diagnostics.Process.GetCurrentProcess(); return p.TotalProcessorTime; }

    /// <summary>The process's CPU over the minute, the part of it no "[CPU]" part covers (WPF's render thread, the runtime, the
    /// finalizer, Windows OCR's own threads), the GC's pauses, its collections by generation and the memory allocated.</summary>
    static string Whole(double seconds, double partsCpuMs)
    {
        var (cpu, pause, allocated) = (CpuNow(), GC.GetTotalPauseDuration(), GC.GetTotalAllocatedBytes());
        int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        double cpuMs = (cpu - _cpuWas).TotalMilliseconds / seconds;
        string text = $"프로세스 CPU {cpuMs:0.0} ms/s, 측정 밖 {cpuMs - partsCpuMs:0.0}, GC 멈춤 {(pause - _gcPauseWas).TotalMilliseconds / seconds:0.0} ms/s, "
            + $"GC {collections[0] - _collectionsWas[0]}/{collections[1] - _collectionsWas[1]}/{collections[2] - _collectionsWas[2]}, "
            + $"할당 {(allocated - _allocatedWas) / 1048576.0 / seconds:0.0} MB/s";
        (_cpuWas, _gcPauseWas, _allocatedWas) = (cpu, pause, allocated);
        collections.CopyTo(_collectionsWas, 0);
        return text;
    }

    // 리소스 검사: kept only while it is on (null: off), apart from the minute's figures above.
    static Dictionary<string, long>? _watch;
    static readonly Dictionary<string, long> _sinceFrame = [];

    /// <summary>Starts or stops keeping every part's running total (<see cref="Seconds"/>) and the parts since the last frame (<see cref="TakeSinceFrame"/>).</summary>
    public static void Watch(bool on)
    {
        lock (Gate) { _watch = on ? [] : null; _sinceFrame.Clear(); }
    }

    /// <summary>Every part's time since <see cref="Watch"/> started, in seconds (a CPU part under its name plus " [CPU]").</summary>
    public static Dictionary<string, double> Seconds()
    {
        lock (Gate) return (_watch ?? []).ToDictionary(kv => kv.Key, kv => kv.Value / (double)Stopwatch.Frequency);
    }

    /// <summary>The parts' wall ms that ended since the last call (the engine calls it at each captured frame while watching).</summary>
    public static Dictionary<string, double> TakeSinceFrame()
    {
        lock (Gate)
        {
            var taken = _sinceFrame.ToDictionary(kv => kv.Key, kv => kv.Value * 1000.0 / Stopwatch.Frequency);
            _sinceFrame.Clear();
            return taken;
        }
    }

    const string CpuTag = ResourceCheck.CpuTag;   // not in the total: the same time as the wall-clock part it goes with

    /// <summary>This thread's CPU time so far (kernel + user), in 100 ns units.</summary>
    public static long ThreadCpu() =>
        GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user) ? kernel + user : 0;

    /// <summary>Adds a part's wall time and the thread's CPU time over it (from <see cref="ThreadCpu"/> before and after).</summary>
    public static void AddCpu(string part, long wallTicks, long cpu100ns)
    {
        Add(part + CpuTag, cpu100ns * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        Add(part, wallTicks);
    }

    /// <summary>Adds <paramref name="n"/> events of <paramref name="name"/> (shown per second in the [perf] line).</summary>
    public static void Count(string name, long n = 1)
    {
        lock (Gate) Counts[name] = Counts.GetValueOrDefault(name) + n;
    }

    static readonly Dictionary<string, List<double>> Delays = [];

    /// <summary>Adds one sample of a delay (ms; shown as median/95%/max in the [perf] line). The line is written by the next
    /// <see cref="Add"/>, which the engine calls every step.</summary>
    public static void Delay(string name, double ms)
    {
        lock (Gate)
        {
            if (!Delays.TryGetValue(name, out var list)) Delays[name] = list = [];
            list.Add(ms);
        }
    }

    [LibraryImport("kernel32.dll")] private static partial IntPtr GetCurrentThread();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
}
