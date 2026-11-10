using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>When 대난투's number is read and when its 체력 shows (user, 2026-11-09). 대난투 shows no boss HP and no boss name, only
/// the damage dealt, so its number is how anything knows 대난투 from a gone bar: it is read in a 그림자 gate whenever something
/// needs it (the meter: its boss HP and 전투 기록; 숙제's fallback; the display), and the raid's 기믹 체력 switch
/// (<see cref="RaidSettings.ShadowHpOff"/>) hides only the display. Pure.</summary>
public static class ShadowRead
{
    /// <summary><paramref name="gate"/> when its number is read now: a 그림자 gate, with the meter on or the boss HP read wanted
    /// (<see cref="BossHpPlan.Wanted"/>, <paramref name="homework"/> its fallback).</summary>
    public static RaidGate? Gate(RaidSettings? s, RaidGate? gate, IReadOnlyList<RaidGate> gates, bool meterOn, bool homework) =>
        s is not null && gate is { IsShadow: true } && (meterOn || BossHpPlan.Wanted(s, gates, homework)) ? gate : null;

    /// <summary>대난투 체력 shows for <paramref name="gate"/>'s raid (its switch on).</summary>
    public static bool Shown(RaidSettings? s, RaidGate? gate) => gate is not null && s?.ShadowHpOn(gate.Raid) == true;

    /// <summary>The 줄 수 창's lines: the bar's "X n" when read, else 대난투's lines left (no bar shows in it), so the window
    /// counts down live through 대난투 instead of holding the last bar's (user, 2026-11-09).</summary>
    public static int? BarLines(int? read, ShadowHp? gimmick) => read ?? (gimmick is { Dead: false } g ? g.Lines : null);
}

/// <summary>The 그림자 레이드 gimmick (세르카, 벨가르딘; user 2026-11-04): the boss bar and name go and a fill bar shows the damage
/// dealt in it. The boss's HP is then the last bar read (<see cref="Start"/>) less that number (<see cref="Dealt"/>), and its lines
/// that HP over the HP a line held at that read ("X n"). A misread is held off: the number only grows, a jump of over a quarter of
/// the HP counts once it holds (<see cref="Pending"/>), as does a fall well under the count (the count was the misread), and
/// dead needs the same at or past the HP and stays. Pure.</summary>
public sealed record ShadowHp(long Start, long Max, long Dealt, double? PerLine, int Over = 0, long? Pending = null, int Agreed = 0)
{
    /// <summary>Reads alike for a jump or dead to count: at least 0.6 s at the gimmick's 5 reads a second, since a misread can last
    /// a frame or two and a wrong number or a false kill would stick. Unread frames between them don't break the count; a jump
    /// straight past the HP needs both in turn (1.4 s).</summary>
    const int Confirm = 4;
    /// <summary>A number over this many times the HP left at the start is a misread, not damage.</summary>
    const double MaxOver = 2;
    /// <summary>A rise over this share of <see cref="Start"/> in one read needs <see cref="Confirm"/> reads to agree; the video's
    /// fastest was about 7% in 2 s.</summary>
    const double MaxJump = 0.25, Agree = 0.9;

    public long Remaining => Math.Max(0, Start - Dealt);
    public int? Lines => PerLine is > 0 ? (int)Math.Ceiling(Remaining / PerLine.Value) : null;
    /// <summary>The gimmick took the boss's last HP: the fight ends as 클리어 though no bar shows.</summary>
    public bool Dead => Over >= Confirm;

    /// <summary>A gimmick read: <paramref name="lastBar"/> is the last boss bar read before it (current, max, its "X n" or null),
    /// <paramref name="number"/> the gimmick's number now (null: not read).</summary>
    public static ShadowHp? Step(ShadowHp? s, (long Current, long Max, int? Count)? lastBar, long? number)
    {
        if (number is not { } n || lastBar is not var (current, max, count) || current <= 0 || n > current * MaxOver) return s;
        long was = s?.Dealt ?? 0;
        bool jump = s is not null && n - was > current * MaxJump;
        // Well under what was counted: one read is a misread digit, but Confirm in a row mean the count was the misread (세르카 2관
        // 2026-11-09: a read past the HP held 0.8 s and stuck the boss at 1 HP). Dead stays.
        bool drop = s is { Dead: false } && n < was * Agree;
        bool odd = jump || drop;
        int agreed = !odd ? 0 : s?.Pending is { } p && n >= p * Agree && p >= n * Agree ? s.Agreed + 1 : 1;   // the read before said as much
        bool held = odd && agreed < Confirm;
        long dealt = held ? was : drop ? n : Math.Max(was, n);
        long? pending = held ? n : null;
        int over = s?.Dead == true ? s.Over : n >= current && dealt >= current ? (s?.Over ?? 0) + 1 : 0;
        return new(current, max, dealt, count is > 0 ? (double)current / count.Value : null, over, pending, held ? agreed : 0);
    }

    /// <summary>The first read after the number went unread a while with no bar between (2026-11-09: about a dozen times a 대난투):
    /// the same gimmick, so it steps from <paramref name="before"/> and a misread gets the jump check. A read <see cref="Step"/>
    /// skips starts nothing, reads agreeing before the gap count again from one, and a dead count isn't carried, since a false
    /// kill would stick for the rest of it.</summary>
    public static ShadowHp? Resume(ShadowHp? before, (long Current, long Max, int? Count)? lastBar, long? number) =>
        Step(null, lastBar, number) is null ? null
            : Step(before is { Dead: false } b ? b with { Pending = null, Agreed = 0 } : null, lastBar, number);
}

/// <summary>What the 그림자 gimmick's text shows: the boss's HP left and lines (null: no "X n" read) inside the gimmick's fill bar
/// (Geometry.ShadowBar), and the damage dealt in it, easy to read, beside its number (<paramref name="DealtLabel"/>,
/// Geometry.ShadowDealt); screen pixels. Local only, not sent to the party.</summary>
public sealed record ShadowView(long Remaining, int? Lines, PixelRect Bar, long Dealt, PixelRect DealtLabel)
{
    /// <summary>"X 87  4,350억"; the HP alone while no "X n" was read.</summary>
    public string Text
    {
        get
        {
            string hp = $"{Remaining / 100_000_000.0:N0}억";
            return Lines is { } lines ? $"X {lines.ToString(System.Globalization.CultureInfo.InvariantCulture)}  {hp}" : hp;
        }
    }
}
