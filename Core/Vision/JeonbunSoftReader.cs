using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>테스트 모드 강화 읽기 of the 전분 panel (reinforced-reader.plan.md): <see cref="JeonbunReader.ReadSoft"/> checked against
/// the fight itself. On compressed video a blurred digit reads as its look-alike (0 ↔ 9, 3 ↔ 8, 1 ↔ 7); the timer counts one
/// a second and the damage never falls, so a read off the clock takes the clock's timer and a fall is no read. The clock
/// moves to reads off it that tick with each other (a new fight) or hold still for <see cref="StopHold"/> (the timer
/// stopped): a look-alike holds only until the second turns. One per panel.</summary>
public sealed class JeonbunSoftReader
{
    const int Slack = 1;   // s: a read this near the clock is the clock (the second turning between frames)
    static readonly TimeSpan StopHold = TimeSpan.FromSeconds(2);

    // The timer shown is floor(now + c): every read on the clock narrows c to [Lo, Hi).
    (double Lo, double Hi)? _clock;
    long _damage;
    (int Timer, TimeSpan At, TimeSpan Since)? _off;   // the last read off the clock, and since when it holds

    /// <summary>The panel as captured, cleaned three ways (<see cref="Reinforce"/>: levels stretched, then also sharpened,
    /// sharpened only; each wins on some video): the first read on the clock with the damage not falling counts.</summary>
    public JeonbunReading? Read(Image panel, TimeSpan now)
    {
        var levels = Reinforce.Levels(panel);
        Image[] cleaned = [levels, Reinforce.Clean(levels), Reinforce.Clean(panel)];
        return Step(expected => [.. cleaned.Select(p => JeonbunReader.ReadHinted(p, expected))], now);
    }

    /// <summary>A new fight, or 강화 turned on again: the clock and the damage start over.</summary>
    public void Reset() => (_clock, _damage, _off) = (null, 0, null);

    /// <summary>One read: <paramref name="read"/> gives the cleanings' reads for the timer the clock expects (null: none yet),
    /// each with whether its timer is that hint (then it says nothing of the clock).</summary>
    internal JeonbunReading? Step(Func<int?, IReadOnlyList<(JeonbunReading? Reading, bool Hinted)>> read, TimeSpan now)
    {
        double t = now.TotalSeconds;
        int? expected = _clock is var (lo, hi) ? (int)Math.Floor(t + (lo + hi) / 2) : null;
        var reads = read(expected).Where(x => x.Reading is not null).ToList();
        var pick = reads.FirstOrDefault(x => (expected is not { } e || Math.Abs(x.Reading!.Timer - e) <= Slack) && x.Reading!.Damage >= _damage);
        if (pick.Reading is null) pick = reads.FirstOrDefault();
        if (pick.Reading is not { } r) return null;
        if (expected is not { } e || Math.Abs(r.Timer - e) <= Slack) { _off = null; if (!pick.Hinted) Narrow(r.Timer, t); }
        else if (_off is { } o && Ticked(o, r.Timer, now))
            (_off, _clock, _damage) = (null, (r.Timer - t, r.Timer + 1 - t), 0);   // a new fight
        else if (_off is { } still && r.Timer == still.Timer && now - still.Since >= StopHold)
            (_off, _clock) = (null, (r.Timer - t, r.Timer + 1 - t));   // the timer stopped: the same fight, its damage kept
        else
        {
            _off = _off is { } same && same.Timer == r.Timer ? same with { At = now } : (r.Timer, now, now);
            r = r with { Timer = e };
        }
        if (r.Damage < _damage) return null;   // a look-alike digit: the damage only grows
        _damage = r.Damage;
        return r;
    }

    /// <summary>The timer moved on from the last read off the clock by the time between them.</summary>
    static bool Ticked((int Timer, TimeSpan At, TimeSpan Since) off, int timer, TimeSpan now) =>
        timer != off.Timer && Math.Abs(timer - off.Timer - (now - off.At).TotalSeconds) <= Slack;

    void Narrow(int timer, double t)
    {
        var (lo, hi) = (timer - t, timer + 1 - t);
        if (_clock is var (l, h) && Math.Max(l, lo) < Math.Min(h, hi)) (lo, hi) = (Math.Max(l, lo), Math.Min(h, hi));
        _clock = (lo, hi);
    }
}
