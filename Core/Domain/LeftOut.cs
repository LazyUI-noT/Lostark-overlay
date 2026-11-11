namespace GaugeOverlay.Core.Domain;

/// <summary>Seconds left out of 버프 적용률 (dead-disabled.plan.md; user, 2026-11-11): the boss bar unread, every second with
/// no floor, or my skill bar carrying the dead / disabled tint and <see cref="After"/> past it. Such a second adds nothing to
/// my damage, the boss HP lost or the other dealers' figures. Stepped once a second by the meter (<see cref="MeterHistory.Cover"/>)
/// and the recorder (<see cref="FightRecorder"/>). A bar not yet read since <see cref="Clear"/> (a new record) is not unread.
/// Mutable; one thread.</summary>
public sealed class LeftOut
{
    /// <summary>How long after the tint's last second is still left out: getting up, running back.</summary>
    public static readonly TimeSpan After = TimeSpan.FromSeconds(5);
    /// <summary>Reads of a bar that is back before the boss figure counts again: it lags a read behind, and the HP lost
    /// meanwhile comes in one jump.</summary>
    const int Settle = 2;
    bool _seen, _spans;
    int _settling;
    TimeSpan? _tintAt;

    /// <summary>The boss HP lost this second isn't counted: a left-out second, or the figure not yet settled after an unread bar.</summary>
    public bool BossOut { get; private set; }

    public void Clear() => (_seen, _spans, _settling, _tintAt, BossOut) = (false, false, 0, null, false);

    /// <summary>The bar is unread on a second nothing is counted on (the panel or the timer unread too, the whole HUD hidden):
    /// what is counted next was dealt across it, so that second is left out as well.</summary>
    public void Hide()
    {
        if (!_seen) return;
        (_spans, _settling, BossOut) = (true, Settle, true);
    }

    /// <summary>This second: the boss bar <paramref name="unread"/>, my skill bar <paramref name="tinted"/>. True when it is left out.</summary>
    public bool Step(bool unread, bool tinted, TimeSpan now)
    {
        if (tinted) _tintAt = now;
        bool bar = unread && _seen || _spans;
        bool left = bar || _tintAt is { } at && now - at <= After;
        _spans = false;
        if (!unread) _seen = true;
        if (unread && _seen) _settling = Settle;
        BossOut = left || _settling > 0;
        if (!bar && _settling > 0) _settling--;
        return left;
    }
}
