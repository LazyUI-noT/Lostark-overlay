namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 초당 읽기 횟수 (user, 2026-11-03): the read loop takes at most this many steps a second, whatever the monitor's
/// refresh rate. Before, it captured on every new frame while anything was due, so a 144 Hz monitor read ~50% more often than
/// a 60 Hz one (27 → 40 captures a second, skill reads 21 → 57 ms/s). Higher = more accurate, more CPU. Times stay exact
/// (each read keeps its frame's timestamp); only how soon a change is noticed moves (up to 1 / steps s). Pure.</summary>
public static class ReadSteps
{
    public const int Min = 10, Max = 30, Default = 20;   // under 10 the 10/s reads (debuff and 암수 timers, buff row) would slow (review)

    public static int Clamp(int perSecond) => Math.Clamp(perSecond, Min, Max);

    public static TimeSpan Every(int perSecond) => TimeSpan.FromSeconds(1.0 / Clamp(perSecond));

    /// <summary>The slot a step taken at <paramref name="now"/> counts from: the previous slot plus one interval, so frame
    /// lateness doesn't add up (at 144 Hz and 10 steps, a step every 14 or 15 frames, 10 a second); now after a stall
    /// longer than two intervals or for the first step, so missed steps aren't caught up in a burst.</summary>
    public static TimeSpan Next(TimeSpan last, int perSecond, TimeSpan now)
    {
        var every = Every(perSecond);
        return last == TimeSpan.MinValue || now - last >= every + every ? now : last + every;
    }

    /// <summary>How far ahead a step judges what is due: half a step, so a read whose interval is a whole number of steps
    /// runs on its step though its last frame came a little after that step's slot (up to a frame: 7–17 ms).</summary>
    public static TimeSpan Slack(int perSecond) => Every(perSecond) / 2;

    /// <summary>Between steps the loop sleeps at most this long: a cooldown's end still shows within it (under a 60 Hz frame).
    /// It used to sleep 1 ms, about 500 loop turns a second for 20 reads (perf-trace, 2026-11-12).</summary>
    public static readonly TimeSpan IdleMax = TimeSpan.FromMilliseconds(10);

    /// <summary>How long the loop sleeps at <paramref name="now"/> with no step due: until the next step's slot, at most
    /// <see cref="IdleMax"/>, at least 1 ms.</summary>
    public static TimeSpan Idle(TimeSpan last, int perSecond, TimeSpan now)
    {
        var floor = TimeSpan.FromMilliseconds(1);
        if (last == TimeSpan.MinValue) return floor;
        var left = last + Every(perSecond) - now;
        return left < floor ? floor : left > IdleMax ? IdleMax : left;
    }

    /// <summary>Whether a step is due at <paramref name="now"/> after the slot <paramref name="last"/> (MinValue: never).</summary>
    public static bool Due(TimeSpan last, int perSecond, TimeSpan now) => last == TimeSpan.MinValue || now - last >= Every(perSecond);
}
