using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>The gauge value shown. The game sweeps a shine over the gauge (발키리's orb, 발키리아덴버그.mp4 and ...2.mp4):
/// a dark band reads it low for about 3 reads, then a veil reads it full for 2; over 80% the orb also shines on its own,
/// and a full orb reads 0.80 for up to 4 reads. So a value is taken only once <see cref="Run"/> reads in a row (0.5 s at
/// the engine's 100 ms) agree within <see cref="Spread"/>. A shine only ever makes pixels miss once the orb is lit, so
/// the value taken is the run's highest; the gauge drops once a whole run is below it, however little (a run's highest
/// may be a flash read just after the drop: 권왕's Z, 0.03 over an empty orb). Not thread-safe: one per reading thread.</summary>
public sealed class GaugeFilter
{
    /// <summary>Agreeing reads in a row before a value is taken: 5 for an orb (its shine and veils), 3 for 브레이커's ring, whose
    /// plateaus are clean and step about once a second (BreakerSweep ring sweep: only 1–2 read blips).</summary>
    public int Run { get; init; } = 5;
    const double Spread = 0.06;
    /// <summary>How long after a key press its drop may show (the game draws it about 0.4 s after the press).</summary>
    public static readonly TimeSpan ArmFor = TimeSpan.FromSeconds(1);
    // After a confirmed use, reads above it are the skill's effects over the gauge (바드's lightning reads full).
    static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(0.5);
    // Reads in a row near what a use leaves that confirm it: one alone may be a flash over the gauge.
    const int ConfirmRun = 2;
    // SteadyRise: a rise up to this, two reads agreeing, is shown at once (the veil's full reads are a far bigger jump).
    const double SmallRise = 0.25;
    // Predicted: how far a rise may run past what the casts predict.
    const double PredictSlack = 0.05;
    /// <summary>When a rise shows (게이지 읽기). A change drops what the casts before it predicted.</summary>
    public GaugeReadMode Mode { get => _mode; set { if (value != _mode) (_mode, _expected) = (value, 0); } }
    GaugeReadMode _mode;
    /// <summary>The gauge drains steadily after a use (브레이커 수라의길, classes.json "drains"; breaker-aden.plan.md): a fall
    /// over <see cref="DrainRun"/> reads, never rising, at least two steps down, each at most <see cref="DrainStep"/>, is shown
    /// at once instead of waiting for <see cref="Run"/> agreeing reads (which a fall of ~0.03 a read never gives). Off for every
    /// other class, whose shine dips the gauge for a few reads.</summary>
    public bool FollowsDrain { get; set; }
    const int DrainRun = 4;
    const double DrainStep = 0.12, DrainMinFall = 0.02;
    // Leaving full (FollowsDrain only): shown at least Full, then below LeftFull, is one use of the draining skill.
    const double Full = 0.95, LeftFull = 0.9;
    bool _full, _drained;
    readonly Queue<double> _reads = new();
    double? _shown;
    double _left, _margin;
    int _near;
    TimeSpan? _armedUntil;
    TimeSpan _holdUntil;
    double _expected;
    TimeSpan _expectUntil;

    /// <summary>A key press of a skill that would leave <paramref name="left"/>: until <paramref name="until"/>,
    /// <see cref="ConfirmRun"/> reads in a row within <paramref name="margin"/> of it confirm the use, and the read is
    /// shown at once; a press with no such drop (the skill never went off, 바드's free second Z) changes nothing.</summary>
    public void Arm(double left, double margin, TimeSpan until) => (_left, _margin, _armedUntil, _near) = (left, margin, until, 0);

    /// <summary>A use that always spends (도화가): the gauge shows <paramref name="left"/> at once, and the read follows
    /// afterwards. Until <see cref="ArmFor"/> has passed, reads above it (the old gauge before the game draws the drop, the
    /// skill's effects) are left out; if the use never happened, the read takes over again after that.</summary>
    public double Spend(double left, TimeSpan now)
    {
        (_armedUntil, _holdUntil, _near) = (null, now + ArmFor, 0);
        _reads.Clear();
        return (_shown = left).Value;
    }

    /// <summary>Casts just seen should add <paramref name="rise"/> by <paramref name="until"/> (GaugeGains): in <see
    /// cref="GaugeReadMode.Predicted"/>, a rise up to that, two reads agreeing, is shown at once. Casts close together add up.</summary>
    public void Expect(double rise, TimeSpan until) => (_expected, _expectUntil) = (_expected + rise, until > _expectUntil ? until : _expectUntil);

    /// <summary>Whether a press is waiting for its drop at <paramref name="now"/>: the engine reads every frame then.</summary>
    public bool Armed(TimeSpan now) => _armedUntil is { } u && now <= u;

    public double Add(double read, TimeSpan at = default)
    {
        if (Armed(at))
        {
            _near = Math.Abs(read - _left) <= _margin ? _near + 1 : 0;
            if (_near >= ConfirmRun)
            {
                (_armedUntil, _holdUntil) = (null, at + HoldFor);
                _reads.Clear();
                _reads.Enqueue(read);
                return (_shown = read).Value;
            }
        }
        if (at < _holdUntil && read > _shown + Spread) return _shown!.Value;   // the skill's effects over the gauge
        if (at >= _expectUntil) _expected = 0;
        _reads.Enqueue(read);
        if (_reads.Count > Run) _reads.Dequeue();
        if (QuickRise() is { } quick) return Watched(quick);
        if (DrainFall() is { } fall) return Watched(fall);
        if (_reads.Count == Run && _reads.Max() - _reads.Min() <= Spread)
        {
            double v = _reads.Max();
            // Following a drain, the run's highest is an older read of the fall: no rise unless the newest read is higher (수라act's tail).
            bool falling = FollowsDrain && _shown is { } shown && _reads.Last() <= shown;
            if (_shown is not { } s || v > s && !falling || v < s) _shown = v;
        }
        return Watched(_shown ?? read);   // before the first agreeing run: as read
    }

    /// <summary>Whether the gauge left full since the last call (<see cref="FollowsDrain"/> only): the draining skill was used.
    /// Once per fill: it is full again before the next.</summary>
    public bool TakeDrainedFromFull()
    {
        bool drained = _drained;
        _drained = false;
        return drained;
    }

    double Watched(double shown)
    {
        if (!FollowsDrain) return shown;
        if (shown >= Full) _full = true;
        else if (_full && shown < LeftFull) (_full, _drained) = (false, true);
        return shown;
    }

    // The last DrainRun reads fall steadily below the shown value: the newest is shown.
    double? DrainFall()
    {
        if (!FollowsDrain || _shown is not { } s || _reads.Count < DrainRun) return null;
        double[] last = [.. _reads.Skip(_reads.Count - DrainRun)];
        var steps = last.Zip(last.Skip(1), (a, b) => a - b).ToList();
        if (steps.Any(d => d < 0 || d > DrainStep) || steps.Count(d => d > 0) < 2 || last[0] - last[^1] < DrainMinFall || last[^1] >= s) return null;
        return (_shown = last[^1]).Value;
    }

    // The last two reads agree on a rise the mode lets through without the full run: shown at once.
    double? QuickRise()
    {
        double? allowed = Mode switch { GaugeReadMode.SteadyRise => SmallRise, GaugeReadMode.Predicted when _expected > 0 => _expected + PredictSlack, _ => null };
        if (allowed is not { } max || _shown is not { } s || _reads.Count < 2) return null;
        double a = _reads.ElementAt(_reads.Count - 2), b = _reads.Last();
        if (Math.Abs(a - b) > Spread || Math.Min(a, b) <= s || Math.Max(a, b) - s > max + 1e-9) return null;
        double v = Math.Max(a, b);
        _expected = Math.Max(0, _expected - (v - s));
        return (_shown = v).Value;
    }

    /// <summary>Forgets the reads and the value (another character's gauge, or the HUD was hidden).</summary>
    public void Reset() { _reads.Clear(); (_shown, _armedUntil, _holdUntil, _expected, _expectUntil, _full, _drained) = (null, null, default, 0, default, false, false); }
}
