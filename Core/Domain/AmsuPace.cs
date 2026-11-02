using GaugeOverlay.Core.Cooldown;

namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 암흑수류탄 → 읽기 빈도: 높음 reads on the debuff rows' cadence (RowCadence); 낮음 reads far less (user, 2026-11-03:
/// frame drops at 1440p / 144 Hz with 암수 on).</summary>
public enum AmsuRate { 높음, 낮음 }

/// <summary>How often the 암수 row is read (frame passes, and how often its icons are matched again). 낮음: with no bomb, a look
/// once a second; a bomb just found is read closely for <see cref="Lock"/> (its time locks); then one check a second (its slot
/// and timer: a re-throw shows within a second; a new bomb changes the filled count and is matched at once) until
/// <see cref="CloseUnder"/> s are left, where it is read closely again (it runs out, is re-thrown, or the other kind comes).
/// Untimed layouts (no "N초" read) always read 높음: their bombs live only while their icon is seen. Pure.</summary>
public static class AmsuPace
{
    public static readonly TimeSpan Slow = TimeSpan.FromSeconds(1), Lock = TimeSpan.FromSeconds(1);
    public const double CloseUnder = 3;

    /// <param name="bombs">Bombs found at the last pass.</param>
    /// <param name="left">The countdown's least time left (null: none or unknown).</param>
    /// <param name="sinceFound">How long bombs have been found without a break.</param>
    /// <param name="missed">A bomb that is up went unseen at the last frame pass: read closely, so its end (missing 1.5 s from a
    /// visible row, Amsu.Step) rests on as many reads as at 높음, not two (the user's rule: no false disappearing).</param>
    /// <param name="pending">A timer jump (a re-throw) waits for its second agreeing read (Amsu.Seen): read closely, so it
    /// shows within about a second (the user's rule).</param>
    public static (TimeSpan Frames, TimeSpan Classify) Of(AmsuRate rate, bool timed, int bombs, double? left, TimeSpan sinceFound,
        bool missed = false, bool pending = false)
    {
        var high = (RowCadence.FrameEvery, RowCadence.ClassifyEvery);
        if (rate == AmsuRate.높음 || !timed || missed || pending || left is <= CloseUnder) return high;   // the last 3 s even unseen
        if (bombs == 0) return (RowCadence.FrameEvery, Slow);   // with no bomb only identifications run (AmsuWatch.Due)
        // Icons and the boss's HP text are matched on every slow pass too: a moved slot or another boss within a second.
        return sinceFound < Lock || left is null ? high : (Slow, Slow);
    }
}
