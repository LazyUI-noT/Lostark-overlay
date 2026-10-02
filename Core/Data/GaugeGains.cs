using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>What each skill's cast adds to the 아덴 gauge (beta; GaugeReadMode.Predicted), learned from the rise after
/// it. Casts close together share one window, and its rise is taken as the sum of their gains: each cast's gain moves
/// toward that sum by its share of the error (least mean squares), so skills cast together are told apart over many
/// windows. A window where the gauge reached full (the rise cut off) or dropped (a spend) teaches nothing. One per
/// character (클래스 · 직업각인), kept across restarts in gains.json (<see cref="GaugeGainsStore"/>).
/// Not thread-safe: the engine's thread only.</summary>
public sealed class GaugeGains
{
    // The rise after the last cast is taken this long after it (the filter shows it within 0.5 s of the hits landing).
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    static readonly TimeSpan MaxWindow = TimeSpan.FromSeconds(4);
    const int MinSamples = 10;
    const double MinRate = 0.1, Full = 0.99, Drop = 0.06;
    readonly double[] _gain = new double[SlotInfo.All.Length];
    readonly int[] _samples = new int[SlotInfo.All.Length];
    readonly List<SlotId> _casts = [];
    TimeSpan _start, _last;
    double _from, _now, _low, _high;

    /// <summary>Starts from <paramref name="saved"/> (<see cref="Export"/>); a slot name no longer known is left out.</summary>
    public GaugeGains(IReadOnlyDictionary<string, Gain>? saved = null)
    {
        foreach (var (name, g) in saved ?? new Dictionary<string, Gain>())
            if (Enum.TryParse<SlotId>(name, out var s) && double.IsFinite(g.Value) && g.Samples > 0) (_gain[(int)s], _samples[(int)s]) = (g.Value, g.Samples);
    }

    /// <summary>Goes up each time a window teaches something: what has changed since the last save.</summary>
    public int Version { get; private set; }

    /// <summary>What has been learned, by slot name, for <see cref="GaugeGainsStore"/>.</summary>
    public IReadOnlyDictionary<string, Gain> Export() =>
        SlotInfo.All.Where(s => _samples[(int)s] > 0 && double.IsFinite(_gain[(int)s])).ToDictionary(s => s.ToString(), s => new Gain(_gain[(int)s], _samples[(int)s]));

    /// <summary>What a cast of <paramref name="slot"/> is expected to add, or null before <see cref="MinSamples"/> windows.</summary>
    public double? Expected(SlotId slot) => _samples[(int)slot] >= MinSamples ? Math.Max(0, _gain[(int)slot]) : null;

    /// <summary><paramref name="slot"/> was cast at <paramref name="at"/> with the gauge at <paramref name="gauge"/>.</summary>
    public void Cast(SlotId slot, TimeSpan at, double gauge)
    {
        Close(at);
        if (_casts.Count == 0) (_start, _from, _now, _low, _high) = (at, gauge, gauge, gauge, gauge);
        _casts.Add(slot);
        _last = at;
    }

    /// <summary>The gauge shown at <paramref name="at"/>.</summary>
    public void Seen(double gauge, TimeSpan at)
    {
        if (_casts.Count == 0) return;
        (_now, _low, _high) = (gauge, Math.Min(_low, gauge), Math.Max(_high, gauge));
        Close(at);
    }

    void Close(TimeSpan at)
    {
        if (_casts.Count == 0 || (at - _last < Window && at - _start < MaxWindow)) return;
        if (_high < Full && _low >= _from - Drop)
        {
            double error = _now - _from - _casts.Sum(s => _gain[(int)s]);
            foreach (var s in _casts)
            {
                int i = (int)s;
                _samples[i]++;
                _gain[i] += error / _casts.Count * Math.Max(1.0 / _samples[i], MinRate);
            }
            Version++;
        }
        _casts.Clear();
    }
}

/// <summary>A skill's learned gain and how many windows taught it.</summary>
public sealed record Gain(double Value, int Samples);
