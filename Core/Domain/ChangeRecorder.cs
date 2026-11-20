namespace GaugeOverlay.Core.Domain;

/// <summary>When a change of 설정 becomes an undo step. Without a gesture, changes closer than <see cref="MergeSeconds"/> are
/// one step (typing a colour, a held arrow key). A gesture (a slider press to release) is one step however long it
/// pauses: <see cref="BeginGesture"/> arms it, the first change inside records the state before it.</summary>
public sealed class ChangeRecorder(SettingsHistory history, Func<string> snapshot, Func<DateTime>? now = null)
{
    public const double MergeSeconds = 0.6;
    readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    string? _baseline;   // the state after the last change: what the next change records
    DateTime _last;
    bool _gesture, _gestureRecorded;

    /// <summary>The state now is where the next step starts (after a rebuild, a file, an edit from elsewhere).</summary>
    public void Reset() => _baseline = snapshot();

    /// <summary>The next edit is a step of its own (after an undo or redo).</summary>
    public void Separate() { _last = default; _gestureRecorded = false; }

    public void BeginGesture() { _gesture = true; _gestureRecorded = false; }

    public void EndGesture()
    {
        if (!_gesture) return;   // a release on a check box or button is no gesture's end
        _gesture = false;
        _last = _now();
    }

    /// <summary>After a change of a row: records the state before it, unless it belongs to the step in progress.</summary>
    public void Change()
    {
        var t = _now();
        if (_baseline is not null)
        {
            if (_gesture) { if (!_gestureRecorded) { history.Record(_baseline); _gestureRecorded = true; } }
            else if ((t - _last).TotalSeconds >= MergeSeconds) history.Record(_baseline);
        }
        _last = t;
        _baseline = snapshot();
    }

    /// <summary>Before a gesture of its own (a canvas drag, a profile switch, a pick): the state now is a step.</summary>
    public void Step()
    {
        var t = _now();
        if ((t - _last).TotalSeconds >= MergeSeconds) history.Record(_baseline ?? snapshot());
        _last = t;
    }
}
