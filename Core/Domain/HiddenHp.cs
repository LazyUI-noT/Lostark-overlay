using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>2-2 under way: the 전분 record and bar max it belongs to, my damage and the boss HP removed at the first second both
/// read, and my share of what the boss lost since (null until <see cref="HiddenHp.MinSeen"/> of the bar was seen).</summary>
public sealed record ShareTrack(int Fight, long Max, long MyStart, long TakenStart, double? Share);

/// <summary>A ??? gate under way: its max (0.9 × 2-2's), the record it is in, when it began, my 2-2 share (null: none), my
/// damage when it began (null until the panel reads) and the HP removed since.</summary>
public sealed record HiddenHpState(RaidGate Gate, long Max, int Fight, TimeSpan StartedAt, double? Share, long? MyBase, long Removed)
{
    public double RemovedPercent => Max > 0 ? Removed * 100.0 / Max : 0;
    /// <summary>HP % left: what the gate's alerts glow at.</summary>
    public double LeftPercent => 100 - RemovedPercent;
    /// <summary>HP still to remove to reach <see cref="HiddenHp.Target"/> (under 0: past it).</summary>
    public long ToTarget => (long)Math.Round(Max * HiddenHp.Target) - Removed;
}

/// <summary>종막 2-3, EX and 하드 (the user, 2026-11-12; hidden-hp-own-damage.plan.md): the boss HP shows ???. Its max is 0.9 × 2-2's
/// and the goal is to remove 70% of it. In 2-2 my share is my damage (딜러: 피해량, 서포터: 조력 피해량) over the HP the bar
/// lost; in 2-3 the HP removed is my damage since ÷ that share. 에스더 and the party count in the share. Pure.</summary>
public static class HiddenHp
{
    public const double HpOfBefore = 0.9, Target = 0.70, MinSeen = 0.20, NearZero = 0.05;
    /// <summary>A share outside this is a misread (a column that barely moved, a digit too many), not mine.</summary>
    public const double MinShare = 0.01, MaxShare = 1.0;
    /// <summary>The bar not read this long, with the timer running, is 2-3; the share only takes a bar read more recently.</summary>
    public static readonly TimeSpan Unread = TimeSpan.FromSeconds(3);

    /// <summary>The panel's damage column is the one for my role: 조력 피해량 for a support, 피해량 for a dealer.</summary>
    public static bool ColumnFits(MeterRole role, JeonbunReading panel) => panel.Support == (role == MeterRole.서포터);

    /// <summary>The second's step of my 2-2 share: started over by a new record or another bar, moved only while the bar and
    /// my panel (the right column) both read; null while they don't.</summary>
    public static ShareTrack? Track(ShareTrack? t, MeterStatus status, MeterRole role, TimeSpan now)
    {
        if (status.Panel is not { } panel || !ColumnFits(role, panel)) return t is not null && t.Fight == status.Clock.Fight ? t : null;
        if (status.Boss.Max is not { } max || now - status.HpAt >= Unread) return t;
        long taken = BossDamage.Taken(status.Boss);
        if (t is null || t.Fight != status.Clock.Fight || t.Max != max) return new(status.Clock.Fight, max, panel.Damage, taken, null);
        long removed = taken - t.TakenStart, mine = panel.Damage - t.MyStart;
        double? share = removed >= max * MinSeen && mine > 0 ? mine / (double)removed : null;
        return t with { Share = share is >= MinShare and <= MaxShare ? share : null };
    }

    /// <summary>The second's step: starts, moves or ends (null) 2-3. <paramref name="seen"/>: the gate 자동 인식 names (하드 2-2
    /// has no HP in the data). Only my role's column counts (a flip to the other is held), and HP removed never goes back.</summary>
    public static HiddenHpState? Step(HiddenHpState? s, IReadOnlyList<RaidGate> gates, RaidGate? seen, MeterStatus status, ShareTrack? track,
        MeterRole role, TimeSpan now)
    {
        if (s is not null && (status.Clock.Fight != s.Fight || status.HpAt > s.StartedAt)) return null;   // a new record, or a bar reads again
        s ??= Start(gates, seen, status, track, now);
        if (s is null || status.Panel is not { } panel || !ColumnFits(role, panel) || s.Share is not { } share) return s;
        long b = s.MyBase ?? panel.Damage;
        return s with { MyBase = b, Removed = Math.Max(s.Removed, (long)Math.Round(Math.Max(0, panel.Damage - b) / share)) };
    }

    /// <summary>The 줄 수 창's line: "≈45% / 70% · 2500억" (removed / target · left to it; "+800억" past it), or why it can't tell.</summary>
    public static string Label(HiddenHpState s, JeonbunReading? panel, MeterRole role) =>
        panel is null ? "≈? 전분 필요"
        : !ColumnFits(role, panel) ? role == MeterRole.서포터 ? "≈? 조력 피해량 열 필요" : "≈? 피해량 열 필요"
        : s.Share is null ? "≈? 2-2 기록 없음"
        : s.MyBase is null ? "≈?"
        : $"≈{s.RemovedPercent:0}% / {Target * 100:0}% · {(s.ToTarget < 0 ? "+" : "")}{Amount(Math.Abs(s.ToTarget))}";

    static string Amount(long hp) => hp >= 1_000_000_000_000 ? $"{hp / 1e12:0.00}조" : $"{hp / 1e8:0}억";

    static HiddenHpState? Start(IReadOnlyList<RaidGate> gates, RaidGate? seen, MeterStatus status, ShareTrack? track, TimeSpan now)
    {
        if (status.Boss is not { Max: { } max, Last: { } last } || last > max * NearZero) return null;
        if (status.Clock.Frozen || now - status.HpAt < Unread) return null;
        if ((RaidData.ByHp(gates, max)?.Gate ?? seen) is not { } before) return null;
        if (gates.FirstOrDefault(g => g.Hidden && g.After == before.Id) is not { } next) return null;
        double? share = track is not null && track.Fight == status.Clock.Fight && track.Max == max ? track.Share : null;
        return new(next, (long)Math.Round(max * HpOfBefore), status.Clock.Fight, now, share, null, 0);
    }
}
