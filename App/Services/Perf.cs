using System.Diagnostics;

namespace GaugeOverlay.App.Services;

/// <summary>Where the app's own time goes: each part adds the time it took, and once a minute the log gets one "[perf]" line,
/// per part in ms per second (10 ms/s = 1% of one core). Any thread. WPF's own render thread (turning drawings into pixels)
/// and the runtime aren't counted: they are the process's CPU minus these.</summary>
public static class Perf
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(60);
    static readonly object Gate = new();
    static readonly Dictionary<string, long> Ticks = [];
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
                line = $"[perf] {total:0.0} ms/s in all: " + string.Join(", ", Ticks.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} {kv.Value * 1000.0 / Stopwatch.Frequency / seconds:0.0}"));
                Ticks.Clear();
                _since = now;
            }
        }
        if (line is not null) Trace.WriteLine(line);
    }
}
