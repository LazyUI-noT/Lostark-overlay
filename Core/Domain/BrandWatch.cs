using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>낙인's bar (user, 2026-11-15): the boss debuff row read 2/s for the 낙인 icons (<see cref="BrandReader"/>). Icon only, the
/// bar is full while the icon shows and goes <see cref="Grace"/> after it was last seen (a missed read, a cutscene). At 100% and up
/// (<see cref="AmsuLayout.Timed"/>) its "N초" is read too (user, 2026-11-18): once two reads agree the bar drains, its length fitted
/// by <see cref="BuffTracker"/>, and a timed icon that vanishes from a full row (pushed out behind "+N") or an unseen one counts to
/// its end. Holds the
/// row's line, snapped per layout as <see cref="AmsuWatch"/> does (again on each read until a filled row snaps it); captures nothing.</summary>
public sealed class BrandWatch
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(0.5), Grace = TimeSpan.FromSeconds(1.5);

    BuffState _state = BuffState.Absent(BuffKind.낙인, TimeSpan.Zero);
    TimeSpan _readAt = TimeSpan.MinValue, _seenAt;
    AmsuLayout? _layout;
    int _line;
    bool _snapped;
    Image? _sized;
    BuffState _inner = BuffState.Absent(BuffKind.낙인, TimeSpan.Zero);   // BuffTracker's view of the "N초" reads
    int _agree;                    // reads in a row the tracker took as one countdown
    bool _timed, _open;            // the countdown is trusted; the last row had room, so a missing icon is gone
    /// <summary>The longest a 낙인 shows (18 s) and a little over: a read above it is a misread.</summary>
    const double MostSeconds = 20;

    public BuffState State => _state;

    /// <summary>The icon at the last read (the log), null when none showed.</summary>
    public BrandIcon? Icon { get; private set; }

    public bool Due(TimeSpan now) => RowCadence.Elapsed(now, _readAt, Every);

    /// <summary>Reads <paramref name="strip"/> (<see cref="Geometry.BossDebuffStrip"/> at <paramref name="layout"/>) on its own: snaps and
    /// walks the row itself. The engine goes through <see cref="AmsuWatch"/> (one strip, one snap, one walk for both); this is for tests.</summary>
    internal BuffState Read(AmsuLayout layout, Image strip, TimeSpan t)
    {
        var sized = DebuffReader.ToReference(strip, layout.Scale, _sized);
        if (sized != strip) _sized = sized;   // at scale 1 the capture's own buffer comes back: never kept
        if (layout != _layout) (_layout, _line, _snapped) = (layout, layout.Line, false);   // 대난투 flips the row and back
        // An empty row snaps nothing: the placed line until a read with debuffs snaps it (review 2026-11-16).
        if (!_snapped && AmsuReader.SnapIn(sized, layout.Scale, layout.Slots, layout.Line, Geometry.BossLineSnap) is { } snapped)
            (_line, _snapped) = (snapped, true);
        return ReadWalk(layout, sized, strip, AmsuReader.WalkIn(sized, layout.Scale, layout.Slots, _line), _line, t);
    }

    /// <summary>One read over a walk already made on the shared strip: <paramref name="sized"/> is the strip at reference size,
    /// <paramref name="strip"/> the capture (the "N초" is read there), <paramref name="line"/> the row's line.</summary>
    internal BuffState ReadWalk(AmsuLayout layout, Image sized, Image strip, AmsuReader.RowWalk walk, int line, TimeSpan t)
    {
        var filled = walk.Filled.Take(layout.Slots.Reads).ToList();   // 감지 개수 within what the row shows
        // The gate's slots are the walk's: another layout, line or filled count (the row shifted) puts each icon in another one.
        if ((layout, line, filled.Count) != _gated)
        {
            _gated = (layout, line, filled.Count);
            _gate.Reset();
        }
        Filled = filled.Count;
        var hit = BrandReader.FindSlot(sized, layout.Scale, filled, line, _gate);
        _slot = hit?.Slot;
        // ponytail: the timer is looked for under the walk's slot; an icon the finder matched up to 6 px off it still reads (the window is wider).
        double? seconds = hit is { } h && layout.Timed ? AmsuReader.ReadSeconds(strip, h.Slot.Icon, layout.TextScale, MostSeconds) : null;
        return Step(hit?.Icon, seconds, walk.Full, walk.Seen, t);
    }

    AmsuReader.SlotAt? _slot;   // the slot the icon was last found in: the quick timer reads look there

    /// <summary>The countdown is under 2 s (<see cref="RowCadence.Fast"/>): its "N초" is read 10/s between the frame passes.</summary>
    internal bool Fast(TimeSpan now) => _timed && _state.EndsAt is { } end && RowCadence.Fast((end - now).TotalSeconds);

    /// <summary>A quick read of the "N초" under the icon's last slot (a timer pass: no search, the icon was seen on the frame pass).</summary>
    internal void Timer(AmsuLayout layout, Image strip, TimeSpan t)
    {
        if (_slot is { } slot && layout.Timed && _state.Active && AmsuReader.ReadSeconds(strip, slot.Icon, layout.TextScale, MostSeconds) is { } s) Count(s, t);
    }

    readonly IconGate<BrandIcon?> _gate = BrandReader.Gate();   // a slot searched only when its icon changed (HANDOFF 3d)
    (AmsuLayout?, int, int) _gated;                    // the layout, line and filled count the gate's slots belong to

    /// <summary>Every slot searched again (강화 읽기 switched: the strip is prepared differently).</summary>
    public void ResetGate() => _gate.Reset();

    /// <summary>How many slot searches ran (tests).</summary>
    internal int Identifies => _gate.Identifies;

    /// <summary>The debuffs in the row at the last <see cref="Read"/>, 낙인 or not (읽는 자리's boss debuff box turns ✓ on any).</summary>
    public int Filled { get; private set; }

    /// <summary>One read's result: <paramref name="found"/> the icon seen, or null.</summary>
    // ponytail: the bar ends when it goes from the screen (Grace after the last sighting), so the meter and the record count up to
    // Grace + Every past the real end; a tighter end would date it back to the last sighting.
    public BuffState Step(BrandIcon? found, TimeSpan t) => Step(found, null, false, true, t);

    /// <summary><paramref name="seconds"/>: the "N초" under the icon, null when unread; <paramref name="full"/>, <paramref name="seen"/>: the
    /// row held every slot the game shows / was on screen (<see cref="AmsuReader.RowWalk"/>).</summary>
    public BuffState Step(BrandIcon? found, double? seconds, bool full, bool seen, TimeSpan t)
    {
        (_readAt, Icon, _open) = (t, found, seen && !full);
        if (found is null) return Expire(t);
        _seenAt = t;
        if (!_state.Active) (_state, _inner, _agree, _timed) = (new BuffState(BuffKind.낙인, true, null, null, false, t), BuffState.Absent(BuffKind.낙인, t), 0, false);
        if (seconds is { } s) Count(s, t);
        return _state;
    }

    /// <summary>One "N초" read: <see cref="BuffTracker"/> holds the countdown. Until two reads agree (the tracker takes the second
    /// as the same countdown, not a jump) the bar stays icon-only, and a read that doesn't fit starts over: a lone misread never drains it.</summary>
    void Count(double seconds, TimeSpan t)
    {
        var next = BuffTracker.Update(_inner, [new BuffReading(BuffKind.낙인, seconds, t)], t, true);
        if (!_timed)
        {
            if (_inner.Active && (next.Pending is not null || next.Estimated)) { (_inner, _agree) = (BuffState.Absent(BuffKind.낙인, t), 0); return; }
            (_inner, _agree) = (next, _agree + 1);
            _timed = _agree >= 2;
        }
        else _inner = next;
        if (_timed) _state = _state with { EndsAt = _inner.EndsAt, Total = _inner.Total };
    }

    /// <summary>The bar at <paramref name="now"/> with no read. Icon only: gone once <see cref="Grace"/> has passed since the last
    /// sighting. Timed: it counts to its end (row full or unseen: the icon is behind "+N" or the row is hidden), unless the last row
    /// had room and the icon has been gone for <see cref="Grace"/> (then it ended).</summary>
    public BuffState Expire(TimeSpan now)
    {
        if (!_state.Active) return _state;
        bool gone = _timed && _state.EndsAt is { } end
            ? now >= end || (_open && now - _seenAt > Grace)
            : now - _seenAt > Grace;
        if (gone) (_state, _inner, _agree, _timed) = (BuffState.Absent(BuffKind.낙인, now), BuffState.Absent(BuffKind.낙인, now), 0, false);
        return _state;
    }
}
