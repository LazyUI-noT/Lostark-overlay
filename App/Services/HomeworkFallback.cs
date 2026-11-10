using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Services;

/// <summary>숙제's fallback, glued (pyosi-map.plan.md): the boss bar's reads (engine thread) and the game's state, once a second (UI
/// thread), into <see cref="HomeworkWatch"/>; what it decides goes to <see cref="HomeworkService"/>. Off whenever a recorded fight
/// could decide (the meter and 전투 기록 on), in 테스트 모드, or without a bar read: then it starts over, so a read switched off
/// never ends a fight on its last HP.</summary>
public sealed class HomeworkFallback(HomeworkService homework, Func<IReadOnlyList<RaidGate>> gates)
{
    readonly object _lock = new();
    WatchRead? _read;
    HomeworkWatch _watch = HomeworkWatch.None;

    /// <summary>A bar read (any thread); the latest one counts at the next step.</summary>
    public void Read(long current, long max)
    {
        lock (_lock) _read = new WatchRead(current, max);
    }

    /// <summary>Once a second (UI thread). <paramref name="daenantu"/>: 대난투 is up (Snapshot.Daenantu).</summary>
    public void Step(bool active, bool blocked, bool daenantu, bool daenantuKilled, RaidGate? nameGate, TimeSpan now)
    {
        WatchRead? read;
        lock (_lock) (read, _read) = (_read, null);
        if (!active) { _watch = HomeworkWatch.None; return; }
        var (next, decision) = _watch.Step(read, new WatchContext(blocked, nameGate, now, daenantu, daenantuKilled), gates(), homework.Raids);
        _watch = next;
        if (decision is not null) homework.Fallback(decision);
    }
}
