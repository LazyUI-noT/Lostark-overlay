namespace GaugeOverlay.Core.Cooldown;

/// <summary>Which read a debuff row gets this step.</summary>
public enum RowPass { None, Frames, Timers }

/// <summary>The read cadence both debuff rows share: your own (the user's spec, 2026-10-30) and the boss's, for 암수 / 빛암
/// (2026-11-01). A frame pass every <see cref="FrameEvery"/> walks the row with the cheap frame check (the filled slots and the
/// next); its icons are identified again only every <see cref="ClassifyEvery"/>, at once when the filled count changed (the row
/// shifted) or a timed slot's timer went unread, else kept; the timed slots' timers are read on it. A timer pass every
/// <see cref="FastEvery"/> in between reads the timers under <see cref="FastUnder"/> s alone. The walk passes one empty slot (a
/// damage number or a popup over one frame) and ends at <see cref="MissesToEnd"/> in a row. Each row keeps its own slot count.
/// Pure.</summary>
public static class RowCadence
{
    public static readonly TimeSpan FrameEvery = TimeSpan.FromMilliseconds(200), ClassifyEvery = TimeSpan.FromMilliseconds(500),
        FastEvery = TimeSpan.FromMilliseconds(100);

    /// <summary>A timer below this many seconds is read 10 times a second.</summary>
    public const double FastUnder = 2;

    /// <summary>Empty slots in a row that end the walk (one is passed: a frame hidden a moment).</summary>
    public const int MissesToEnd = 2;

    /// <summary>At least <paramref name="every"/> since <paramref name="last"/> (MinValue: never, so due).</summary>
    public static bool Elapsed(TimeSpan now, TimeSpan last, TimeSpan every) => last == TimeSpan.MinValue || now - last >= every;

    /// <summary>The pass due at <paramref name="now"/>: frames when due, else timers when a timer is fast and due.</summary>
    public static RowPass Due(TimeSpan framesAt, TimeSpan timersAt, bool fastTimers, TimeSpan now) =>
        Elapsed(now, framesAt, FrameEvery) ? RowPass.Frames
        : fastTimers && Elapsed(now, timersAt, FastEvery) ? RowPass.Timers
        : RowPass.None;

    /// <summary>Whether a frame pass that found <paramref name="filled"/> slots (<paramref name="wasFilled"/> before) identifies
    /// their icons again.</summary>
    public static bool Identify(int wasFilled, int filled, TimeSpan classifiedAt, bool timerUnread, TimeSpan now) =>
        filled != wasFilled || Elapsed(now, classifiedAt, ClassifyEvery) || timerUnread;

    /// <summary>A last read that puts a timer on the fast pass.</summary>
    public static bool Fast(double? seconds) => seconds is < FastUnder;

    /// <summary>The walk over <paramref name="count"/> slots: <paramref name="isFilled"/> asked from the left until
    /// <see cref="MissesToEnd"/> empty in a row; the filled count is the last filled slot + 1 (a passed empty slot inside).</summary>
    public static int Filled(int count, Func<int, bool> isFilled)
    {
        int filled = 0, empty = 0;
        for (int i = 0; i < count && empty < MissesToEnd; i++)
            if (isFilled(i)) (filled, empty) = (i + 1, 0);
            else empty++;
        return filled;
    }
}
