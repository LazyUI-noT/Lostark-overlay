namespace GaugeOverlay.Core.Vision;

/// <summary>The boss HP read checked against the fight (user, 2026-11-17), so a few spikes (a mouse cursor over the text, a
/// misread digit) don't reach the label or the record. The rule is 강화 읽기's (<see cref="BossHpSoftReader.Fits"/>): the max
/// holds and the HP only falls, at most 5% of the max a second; the shield only falls. A read that breaks it is dropped unless
/// the next read agrees (a new boss or phase, a heal, a new shield). While reads are dropped, or fail with the bar still up
/// (its "X n" read), the last good read is kept for <see cref="Hold"/>; then there is none, as with no bar. Not used under
/// 강화 읽기, which checks its own. One per boss bar; <see cref="Reset"/> at a new gate and on leaving the dungeon.</summary>
public sealed class BossHpFight
{
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(2);

    (long Current, long Max, long Shield, TimeSpan At)? _last;
    // The last read that broke the fight: a second that agrees with it or falls on from it (a small boss falling faster than
    // the rule allows) starts a new one.
    (long Current, long Max, long Shield, TimeSpan At)? _odd;

    /// <summary>The last <see cref="Step"/> gave the kept read, not a new one: not a sighting of the HP (the label's own hold,
    /// 숙제, the 그림자 gimmick's start read only new ones).</summary>
    public bool Held { get; private set; }

    /// <param name="barUp">The bar is still there although its HP didn't read (its count read): the last good read is kept.</param>
    public (long Current, long Max, long Shield)? Step((long Current, long Max, long Shield)? read, bool barUp, TimeSpan now)
    {
        Held = false;
        if (read is var (current, max, shield) && Accept(current, max, shield))
        {
            _last = (current, max, shield, now);
            return read;
        }
        if (_last is not var (c, m, s, at) || now - at > Hold || read is null && !barUp) return null;
        Held = true;
        return (c, m, s);

        bool Accept(long current, long max, long shield)
        {
            // Agrees too: a misread a little under the HP, kept, mustn't hold back the right read after it.
            if (_last is not { } last || FitsAfter(last, current, max, shield, now) || Agrees(last, current, max, shield))
            {
                _odd = null;
                return true;
            }
            // Falling on from a fall, at any rate: a spike above the HP never chains, and one under it is followed by a higher
            // read (the right one), so only a second misread lower still gets through.
            bool again = _odd is { } odd && (odd.Max == max && odd.Max == last.Max && odd.Current <= last.Current
                && current <= odd.Current && shield <= odd.Shield || Agrees(odd, current, max, shield));
            _odd = again ? null : (current, max, shield, now);
            return again;
        }
    }

    static bool FitsAfter((long Current, long Max, long Shield, TimeSpan At) was, long current, long max, long shield, TimeSpan now) =>
        BossHpSoftReader.Fits((was.Current, was.Max, was.At), current, max, now) && shield <= was.Shield;

    static bool Agrees((long Current, long Max, long Shield, TimeSpan At) was, long current, long max, long shield) =>
        was.Max == max && Math.Abs(was.Current - current) <= BossHpSoftReader.Agree * max
        && Math.Abs(was.Shield - shield) <= BossHpSoftReader.Agree * max;

    public void Reset() => (_last, _odd) = (null, null);
}
