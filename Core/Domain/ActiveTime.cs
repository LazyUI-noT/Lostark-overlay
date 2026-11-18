namespace GaugeOverlay.Core.Domain;

/// <summary>How long something was active (리소스 검사, user 2026-11-18: a box's CPU is averaged over the time it ran, so
/// 암수 in town doesn't water it down): each run is active from its start to <paramref name="grace"/> seconds after its end,
/// overlapping runs merged. The last run's grace counts in full even before it is over, so a box that has only just run
/// isn't divided by a sliver (ResourceCheck caps it at the check's length). Times are seconds on one clock; not thread-safe
/// (Perf calls it under its lock).</summary>
public sealed class ActiveTime(double grace)
{
    double _closed, _from, _to = double.NaN;   // [_from, _to]: the open stretch, not yet in _closed

    public void Hit(double start, double end)
    {
        double to = end + grace;
        if (double.IsNaN(_to)) (_from, _to) = (start, to);
        else if (start <= _to) _to = Math.Max(_to, to);   // overlaps the stretch (or another thread's run ended a moment earlier)
        else { _closed += _to - _from; (_from, _to) = (start, to); }
    }

    /// <summary>Seconds active so far.</summary>
    public double Seconds => double.IsNaN(_to) ? 0 : _closed + _to - _from;
}
