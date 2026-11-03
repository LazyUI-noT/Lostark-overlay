using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>How the boss row reads at the current layout: the icons' scale, the "N초" scale, the debuff slots in the strip, and
/// whether the time is read at all (<see cref="Amsu.Timed"/>), and the row's line in the strip when it is placed (the engine's
/// <see cref="Geometry.BossDebuffStrip"/>: HUD and resolution set it, user 2026-11-04); null: found in the strip (a search strip).</summary>
public sealed record AmsuLayout(double Scale, double TextScale, BossSlots Slots, bool Timed, int? Line = null)
{
    public static AmsuLayout Of(Viewport vp, Fit fit, int bossSize) =>
        new(Geometry.BossScale(vp, fit, bossSize), Geometry.BuffScale(vp, bossSize), Geometry.BossSlotsIn(vp, fit, bossSize), Amsu.Timed(vp, bossSize),
            Geometry.BossDebuffLine);
}

/// <summary>The 암수 read loop (amsu.design.md) as the engine runs it on its captures and the dev sweep on a video, on the cadence
/// both debuff rows share (<see cref="RowCadence"/>): a frame pass 5/s walks the row's settled line (<see cref="RowLock"/>); its
/// bombs are identified again 2/s (with the boss's HP text), or at once when the filled count changed or a timer went unread,
/// else kept and moved with their slots; their "N초" is read on each frame pass, and 10/s between them under 2 s. Then the
/// countdown (<see cref="Amsu.Step"/>). Holds the loop's state; captures nothing.</summary>
public sealed class AmsuWatch
{
    RowLock _line = RowLock.None;
    IReadOnlyList<BombAt> _found = [];   // the bombs' icons at the last pass: the timer reads look there
    bool _full, _seen;                   // at the last pass: the row full (a bomb missing may be behind "+N"), and on screen
    int _filled;                         // the filled slots at the last pass
    bool _unread;                        // a timed bomb's "N초" unread on the last frame pass: identify again
    Dictionary<Bomb, double> _seconds = [];   // each bomb's last "N초" read: under 2 s, the timer pass reads it
    AmsuRow? _last;                      // the last row read: the window redraws only when it changes
    AmsuLayout? _layout;                 // the layout the line was learnt at
    TimeSpan _searched = TimeSpan.MinValue, _framesAt = TimeSpan.MinValue, _timersAt = TimeSpan.MinValue;
    TimeSpan? _foundSince;               // since when bombs have been found without a break (AmsuPace's lock)

    /// <summary>설정's 읽기 빈도 (AmsuPace): 높음 reads on the shared cadence, 낮음 far less.</summary>
    public AmsuRate Rate { get; set; } = AmsuRate.높음;

    public AmsuState State { get; private set; } = AmsuState.None;

    /// <summary>When the bombs were last identified (matched against the templates).</summary>
    internal TimeSpan ClassifiedAt { get; private set; } = TimeSpan.MinValue;

    /// <summary>The last row read (the dev sweep's log).</summary>
    public AmsuRow? LastRow => _last;

    /// <summary>The row's settled line (strip px), for the log.</summary>
    public int? Line => LineY;

    /// <summary>The line walked: the placed one, snapped once to the frames (<see cref="Geometry.BossLineSnap"/>), else the settled one.</summary>
    int? LineY => _layout?.Line is { } placed ? placed + _snap : _line.Y;
    int _snap;          // px the placed line moved to sit on the frames
    bool _snapped;      // the snap is done for this layout

    /// <summary>An identification due whatever the walk finds: by time, or a timed bomb's "N초" unread (the boss's HP text with it).</summary>
    bool IdentifyDue(TimeSpan now, TimeSpan classifyEvery) => _unread || RowCadence.Elapsed(now, ClassifiedAt, classifyEvery);

    /// <summary>The cadence at <paramref name="now"/> (AmsuPace): the 읽기 빈도, the bombs found and the countdown's least time left.</summary>
    (TimeSpan Frames, TimeSpan Classify) Pace(TimeSpan now, bool timed)
    {
        double? left = State.Up.Count == 0 ? null : State.Up.Values.Min(b => (b.EndsAt - now).TotalSeconds);
        // Unseen at the last frame pass on a row that showed and had room (behind "+N" or in a cutscene it is rightly unseen).
        bool missed = _framesAt != TimeSpan.MinValue && _seen && !_full && State.Up.Values.Any(b => b.LastSeen < _framesAt);
        bool pending = State.Up.Values.Any(b => b.Pending is not null);   // a timer jump waiting for its second read
        return AmsuPace.Of(Rate, timed, _found.Count, left, _foundSince is { } f ? now - f : TimeSpan.Zero, missed, pending);
    }

    /// <summary>The pass due at <paramref name="now"/>, and whether it reads the boss's HP text (a frame pass due to identify).</summary>
    public (RowPass Pass, bool BossText) Due(TimeSpan now, bool timed)
    {
        bool fast = timed && _found.Any(Fast);
        var (frames, classify) = Pace(now, timed);
        var pass = RowCadence.Due(_framesAt, _timersAt, fast, now, frames);
        // No line to walk yet, or no bomb in the row: the next identification finds a new one (within 0.5 s at 높음: user,
        // 2026-11-01; 1 s at 낮음).
        if (pass == RowPass.Frames && (LineY is null || _found.Count == 0) && !IdentifyDue(now, classify)) pass = RowPass.None;
        return (pass, pass == RowPass.Frames && IdentifyDue(now, classify));
    }

    /// <summary>One <paramref name="pass"/> at <paramref name="t"/> with the boss's max HP (null: unread or not read this pass).
    /// Returns the countdown's changes and whether the window needs a redraw.</summary>
    public (IReadOnlyList<AmsuEdge> Edges, bool Changed) Read(AmsuLayout layout, RowPass pass, long? boss, Image? strip, TimeSpan t)
    {
        AmsuRow? row = pass switch
        {
            RowPass.Frames when strip is not null => Frames(layout, boss, strip, t),
            RowPass.Timers when strip is not null && layout.Timed => Timers(layout, strip, t),
            _ => null,
        };
        var was = State;
        State = Amsu.Step(State, row, boss, t, iconsOnly: !layout.Timed);
        bool read = row is not null && !(_last?.Bombs.SequenceEqual(row.Bombs) ?? row.Bombs.Count == 0);
        if (row is not null) _last = row;
        return (Amsu.Edges(was, State, t), read || was.BossMaxHp != State.BossMaxHp || was.Up.Count != State.Up.Count);
    }

    AmsuRow? Frames(AmsuLayout layout, long? boss, Image strip, TimeSpan t)
    {
        var (_, classify) = Pace(t, layout.Timed);
        bool bossRead = IdentifyDue(t, classify);
        (_framesAt, _timersAt) = (t, t);
        if (layout != _layout) (_layout, _line, _snap, _snapped) = (layout, RowLock.None, 0, false);   // another HUD, size or resolution: placed again
        // The strip at reference size, once for the pass: the walk and the identification both use it.
        Image? sized = null;
        Image Sized() => sized ??= DebuffReader.ToReference(strip, layout.Scale);
        // Identifying due by time or an unread timer: no walk first (the match walks the row itself).
        var walk = !bossRead && LineY is { } y ? AmsuReader.WalkIn(Sized(), layout.Scale, layout.Slots, y) : null;
        bool identify = walk is null || RowCadence.Identify(_filled, walk.Filled.Count, ClassifiedAt, _unread, t, classify);
        // A boss read starts the search; between boss reads a walked row goes on. Untimed, a bomb already up keeps it going
        // while a popup hides the HP text (review 2026-11-01); timed, the countdown runs on through it (a cutscene hides the
        // bar, the bomb doesn't end).
        bool allowed = boss is not null || !bossRead && walk is not null || State.Up.Count > 0 && !layout.Timed;
        // Due but the HP text unread: the bombs found go on along the settled line, as between identifications (replay 14: an
        // unread "N초" asked for one every pass, each dropped the bombs, and they went unseen until a missed match ended one).
        if (identify && !allowed && _found.Count > 0 && LineY is { } kept)
            (walk, identify) = (AmsuReader.WalkIn(Sized(), layout.Scale, layout.Slots, kept), false);
        if (identify)
        {
            if (!allowed)
            {
                (_found, _unread) = ([], false);
                return null;
            }
            // Without a boss read only the settled line is matched: a search could take another row's icon for a bomb.
            // A placed line: snapped once to the boss's debuff frames, then read there alone; no search.
            if (layout.Line is { } placed && !_snapped && AmsuReader.SnapIn(Sized(), layout.Scale, layout.Slots, placed, Geometry.BossLineSnap) is { } snapped)
                (_snap, _snapped) = (snapped - placed, true);
            bool search = layout.Line is null && boss is not null && AmsuSchedule.SearchLines(_line.Y is not null, Since(t, _searched), State.Up.Count > 0);
            if (search) _searched = t;
            var find = AmsuReader.LocateIn(Sized(), layout.Scale, layout.Slots, LineY, search);
            // A bomb's line, else the line of the boss's red-framed debuffs: only the game's settings move the row (user,
            // 2026-11-04), so it settles before any bomb and the candidates aren't searched on every identification.
            // Once settled, only a bomb moves it: a frames-only read of the held line would reset a move a bomb elsewhere began.
            if (layout.Line is null) _line = _line.Next(find.Bombs.Count > 0 ? find.Y : _line.Y is null ? find.FramedY : null);
            (_found, _full, _seen, _filled, ClassifiedAt) = (find.Bombs, find.Full, find.Seen, find.Filled, t);
        }
        else if (walk is not null)   // the same row (no identification means a walk): the bombs kept, each moved to its slot's box (gone with its slot)
        {
            _found = [.. _found.Select(b => walk.Filled.FirstOrDefault(f => f.Slot == b.Slot) is { } f ? b with { Icon = f.Icon } : null)
                .OfType<BombAt>()];
            (_full, _seen, _filled) = (walk.Full, walk.Seen, walk.Filled.Count);
        }
        _foundSince = _found.Count == 0 ? null : _foundSince ?? t;
        var row = Read(strip, layout, _found, _ => true);
        _unread = layout.Timed && row.Bombs.Any(b => b.Seconds is null);
        _seconds = [];
        foreach (var b in row.Bombs.Where(b => b.Seconds is not null)) _seconds.TryAdd(b.Kind, b.Seconds!.Value);   // a kind read twice: the first
        return row;
    }

    AmsuRow Timers(AmsuLayout layout, Image strip, TimeSpan t)
    {
        _timersAt = t;
        var row = Read(strip, layout, _found, Fast);   // the others unread this pass
        foreach (var b in row.Bombs.Where(b => b.Seconds is not null)) _seconds[b.Kind] = b.Seconds!.Value;
        return row;
    }

    bool Fast(BombAt b) => _seconds.TryGetValue(b.Kind, out double s) && RowCadence.Fast(s);

    AmsuRow Read(Image strip, AmsuLayout layout, IReadOnlyList<BombAt> bombs, Func<BombAt, bool> timer) =>
        new([.. bombs.Select(b => new BombRead(b.Kind, layout.Timed && timer(b) ? AmsuReader.ReadSeconds(strip, b, layout.TextScale) : null, b.Slot))],
            _full, _seen);

    static TimeSpan Since(TimeSpan now, TimeSpan last) => last == TimeSpan.MinValue ? TimeSpan.MaxValue : now - last;
}
