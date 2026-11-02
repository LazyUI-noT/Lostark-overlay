using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public static T Run<T>(string part, Func<T> work)
    {
        long t0 = Stopwatch.GetTimestamp();
        try { return work(); }
        finally { Add(part, Stopwatch.GetTimestamp() - t0); }
    }

    public static void Run(string part, Action work) => Run(part, () => { work(); return 0; });

    public static void Add(string part, long ticks)
    {
        string? line = null;
        lock (Gate)
        {
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
                Ticks.Clear();
                Counts.Clear();
                _since = now;
            }
        }
        if (line is not null) Trace.WriteLine(line);
    }

    const string CpuTag = " [CPU]";   // not in the total: the same time as the wall-clock part it goes with

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

    [LibraryImport("kernel32.dll")] private static partial IntPtr GetCurrentThread();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
}
