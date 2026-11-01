using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>How the boss row reads at the current layout: the icons' scale, the "N초" scale, the debuff slots in the strip, and
/// whether the time is read at all (<see cref="Amsu.Timed"/>).</summary>
public sealed record AmsuLayout(double Scale, double TextScale, BossSlots Slots, bool Timed)
{
    public static AmsuLayout Of(Viewport vp, Fit fit, int bossSize) =>
        new(Geometry.BossScale(vp, fit, bossSize), Geometry.BuffScale(vp, bossSize), Geometry.BossSlotsIn(vp, fit, bossSize), Amsu.Timed(bossSize));
}

/// <summary>The 암수 read loop (amsu.design.md) as the engine runs it on its captures and the dev sweep on a video: when to read
/// (<see cref="AmsuSchedule"/>), the row's line settled over finds (<see cref="RowLock"/>), the bombs and their "N초", and the
/// countdown (<see cref="Amsu.Step"/>). Holds the loop's state; captures nothing.</summary>
public sealed class AmsuWatch
{
    RowLock _line = RowLock.None;
    IReadOnlyList<BombAt> _found = [];   // the bombs' icons at the last icon read: the timer reads look there
    bool _full, _seen;                   // at the last icon read: the row full (a bomb missing may be behind "+N"), and on screen
    AmsuRow? _last;                      // the last row read: the window redraws only when it changes
    TimeSpan _searched = TimeSpan.MinValue, _icons = TimeSpan.MinValue, _timer = TimeSpan.MinValue;

    public AmsuState State { get; private set; } = AmsuState.None;

    /// <summary>The last row read (the dev sweep's log).</summary>
    public AmsuRow? LastRow => _last;

    /// <summary>The row's settled line (strip px), for the log.</summary>
    public int? Line => _line.Y;

    /// <summary>What is due at <paramref name="now"/>: an icon read (with the boss's HP text), and a timer read between them.</summary>
    public (bool Icons, bool Timer) Due(TimeSpan now, bool timed)
    {
        var view = Amsu.View(State, now);
        var (icons, timer) = AmsuSchedule.Due(view.Shown is null ? null : view.Left, Since(now, _icons), Since(now, _timer));
        return (icons, timer && timed && _found.Count > 0);
    }

    /// <summary>One read at <paramref name="t"/>: an icon read (<paramref name="iconRead"/>) with the boss's max HP (null: unread),
    /// else a timer read under the bombs found last. Returns the countdown's changes and whether the window needs a redraw.</summary>
    public (IReadOnlyList<AmsuEdge> Edges, bool Changed) Read(AmsuLayout layout, bool iconRead, long? boss, Image? strip, TimeSpan t)
    {
        AmsuRow? row = null;
        if (iconRead)   // is a boss up, where is its row, which bombs are in it
        {
            _icons = t;
            _found = [];
            // A boss read starts the search. Untimed, a bomb already up keeps it going while a popup hides the HP text (review
            // 2026-11-01); timed, the countdown runs on through it (a cutscene hides the bar, the bomb doesn't end).
            if (strip is not null && (boss is not null || State.Up.Count > 0 && !layout.Timed))
            {
                bool search = AmsuSchedule.SearchLines(_line.Y is not null, Since(t, _searched), State.Up.Count > 0);
                if (search) _searched = t;
                var (y, found, full, seen) = AmsuReader.Locate(strip, layout.Scale, layout.Slots, _line.Y, search);
                _line = _line.Next(found.Count > 0 ? y : null);   // a line counts only where a bomb was found on it
                (_found, _full, _seen) = (found, full, seen);
                row = Seconds(strip, layout, t);
            }
        }
        else if (strip is not null && layout.Timed) row = Seconds(strip, layout, t);
        var was = State;
        State = Amsu.Step(State, row, boss, t, iconsOnly: !layout.Timed);
        bool read = row is not null && !(_last?.Bombs.SequenceEqual(row.Bombs) ?? row.Bombs.Count == 0);
        if (row is not null) _last = row;
        return (Amsu.Edges(was, State, t), read || was.BossMaxHp != State.BossMaxHp || was.Up.Count != State.Up.Count);
    }

    AmsuRow Seconds(Image strip, AmsuLayout layout, TimeSpan t)
    {
        if (_found.Count > 0 && layout.Timed) _timer = t;
        return new([.. _found.Select(b => new BombRead(b.Kind, layout.Timed ? AmsuReader.ReadSeconds(strip, b, layout.TextScale) : null, b.Slot))], _full, _seen);
    }

    static TimeSpan Since(TimeSpan now, TimeSpan last) => last == TimeSpan.MinValue ? TimeSpan.MaxValue : now - last;
}
