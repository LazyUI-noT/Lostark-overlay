namespace GaugeOverlay.Core.Domain;

/// <summary>The 그림자 레이드 gimmick (세르카, 벨가르딘; user 2026-11-04): the boss bar and name go and a fill bar shows the damage
/// dealt in it. The boss's HP is then the last bar read (<see cref="Start"/>) less that number (<see cref="Dealt"/>), and its lines
/// that HP over the HP a line held at that read ("X n"). A misread is held off: the number only grows, a jump of over a quarter of
/// the HP counts once the next read agrees (<see cref="Pending"/>), and dead needs two reads at or past the HP and stays. Pure.</summary>
public sealed record ShadowHp(long Start, long Max, long Dealt, double? PerLine, int Over = 0, long? Pending = null)
{
    /// <summary>Reads at or past the boss's HP before it counts as dead: one could be a misread.</summary>
    const int DeadAfter = 2;
    /// <summary>A number over this many times the HP left at the start is a misread, not damage.</summary>
    const double MaxOver = 2;
    /// <summary>A rise over this share of <see cref="Start"/> in one read (0.5 s) needs the next read to agree; the video's fastest
    /// was about 7% in 2 s.</summary>
    const double MaxJump = 0.25, Agree = 0.9;

    public long Remaining => Math.Max(0, Start - Dealt);
    public int? Lines => PerLine is > 0 ? (int)Math.Ceiling(Remaining / PerLine.Value) : null;
    /// <summary>The gimmick took the boss's last HP: the fight ends as 클리어 though no bar shows.</summary>
    public bool Dead => Over >= DeadAfter;

    /// <summary>A gimmick read: <paramref name="lastBar"/> is the last boss bar read before it (current, max, its "X n" or null),
    /// <paramref name="number"/> the gimmick's number now (null: not read).</summary>
    public static ShadowHp? Step(ShadowHp? s, (long Current, long Max, int? Count)? lastBar, long? number)
    {
        if (number is not { } n || lastBar is not var (current, max, count) || current <= 0 || n > current * MaxOver) return s;
        long was = s?.Dealt ?? 0;
        bool jump = s is not null && n - was > current * MaxJump;
        bool agreed = s?.Pending is { } p && n >= p * Agree;   // the read before said as much
        long dealt = jump && !agreed ? was : Math.Max(was, n);
        long? pending = jump && !agreed ? n : null;
        int over = s?.Dead == true ? s.Over : n >= current && dealt >= current ? (s?.Over ?? 0) + 1 : 0;
        return new(current, max, dealt, count is > 0 ? (double)current / count.Value : null, over, pending);
    }
}

/// <summary>What the 그림자 gimmick's text shows: the boss's HP left and lines (null: no "X n" read), at the boss bar's place on
/// screen (Geometry.BossBar). Local only, not sent to the party.</summary>
public sealed record ShadowView(long Remaining, int? Lines, PixelRect Bar);
