using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 레이드 for a ??? gate: the support's 조력 피해량 share of its party's damage (%), the other party as a multiple of
/// the known one (when only one party's figures arrive), the 에스더 plan for this gate (uses at HP %; empty: 2-1's share of HP, at
/// <see cref="HiddenHp.FallbackAt"/>), and each party's support 조력 피해량 at the end of the calibration gate (2-1; 억, 0 = unknown).</summary>
public sealed record HiddenHpOptions(double AssistShare, double OtherParty, IReadOnlyList<EstherUse> Esthers, double Assist1 = 0, double Assist2 = 0);

/// <summary>A ??? gate under way: its estimated max and HP left, the fight (전분 record) it is in, the raid damage when it began
/// (null until the first figures), the gimmick's drop (0 until it happens) and where the gimmick is, and the raid damage last
/// seen with when it last changed (the gimmick's pause is the damage standing still).</summary>
public sealed record HiddenHpState(RaidGate Gate, string Difficulty, long Max, long Left, int Fight, TimeSpan StartedAt, long? Base, long Drop,
    GimmickPhase Phase = GimmickPhase.None, long LastRaid = 0, TimeSpan LastChange = default)
{
    public double Percent => Max > 0 ? Left * 100.0 / Max : 0;
}

/// <summary>종막 EX 2-3 (the user's rules): the boss HP shows ???, so HP left ≈ max − the raid's damage since 2-3 began − the
/// planned 에스더 − the gimmick (at 25% the boss goes to the centre, everyone dies and revives, and it is at 10%: not damage). It
/// begins when the gate before it (its <see cref="RaidGate.After"/>) was read near 0 and its HP then stops reading while the
/// 전분 timer runs; its difficulty is that HP's. Pure.</summary>
/// <summary>2-3's gimmick: not yet at 25%; at 25% (the boss heads to the centre: damage goes on until it stops); paused (everyone
/// down and reviving: the raid's damage stands still; the 광폭화 timer is frozen but the 전분's runs); done (damage again: at 10%).</summary>
public enum GimmickPhase { None, Waiting, Paused, Done }

public static class HiddenHp
{
    /// <summary>The raid's damage standing still this long after 25% is the gimmick's pause.</summary>
    public static readonly TimeSpan PauseFor = TimeSpan.FromSeconds(5);

    public const double NearZero = 0.05, Gimmick = 0.25, AfterGimmick = 0.10;
    /// <summary>With no 에스더 plan, 2-1's 에스더 share goes in at this HP %.</summary>
    public const int FallbackAt = 50;
    public static readonly TimeSpan Unread = TimeSpan.FromSeconds(3);

    /// <summary>A party's damage: its three dealers' when all send, else its support's 조력 피해량 × 100 ÷ the share; null: neither.</summary>
    public static long? PartyDamage(IReadOnlyList<MeterSample> party, double assistShare)
    {
        var dealers = party.Where(s => s.Role == MeterRole.딜러).ToList();
        if (dealers.Count >= 3) return dealers.Sum(d => d.Damage);
        return party.FirstOrDefault(s => s.Role == MeterRole.서포터) is { } support && assistShare > 0
            ? (long)Math.Round(support.Damage * 100 / assistShare) : null;
    }

    /// <summary>Both parties' damage; with one known, the other is it × the parties' ratio at the end of 2-1 (<paramref name="assist1"/>,
    /// <paramref name="assist2"/>: each party's 조력 피해량 then), or × <paramref name="otherParty"/> without it; null: neither known.</summary>
    public static long? RaidDamage(IReadOnlyList<MeterSample> all, int myParty, double assistShare, double otherParty, double assist1 = 0, double assist2 = 0)
    {
        long? mine = PartyDamage([.. all.Where(s => s.Party == myParty)], assistShare);
        long? other = PartyDamage([.. all.Where(s => s.Party != myParty)], assistShare);
        var (cMine, cOther) = myParty == 2 ? (assist2, assist1) : (assist1, assist2);
        bool ratio = cMine > 0 && cOther > 0;
        return (mine, other) switch
        {
            ({ } a, { } b) => a + b,
            ({ } a, null) => a + (long)Math.Round(a * (ratio ? cOther / cMine : otherParty)),
            (null, { } b) => b + (long)Math.Round(b * (ratio ? cMine / cOther : otherParty)),
            _ => null,
        };
    }

    /// <summary>The 에스더's share of the calibration gate's HP: its max − the players' damage ((both parties' 조력 피해량, 억) × 100 ÷
    /// the share), over its max; null while a party's figure is unknown.</summary>
    public static double? EstherShare(long gateMax, double assist1, double assist2, double assistShare) =>
        gateMax > 0 && assist1 > 0 && assist2 > 0 && assistShare > 0
            ? Math.Max(0, 1 - (assist1 + assist2) * 1e8 * 100 / assistShare / gateMax) : null;

    /// <summary>The 조력 비율 2-1 shows: both parties' 조력 피해량 over what the players dealt (its HP − its 에스더 plan, 억); null while
    /// a figure is missing or the 에스더 leaves nothing for the players.</summary>
    public static double? CalibratedShare(long gateMax, double assist1, double assist2, double esther) =>
        assist1 > 0 && assist2 > 0 && esther > 0 && gateMax - esther * 1e8 is var players && players > 0
            ? (assist1 + assist2) * 1e8 * 100 / players : null;

    /// <summary>The 조력 비율 to use for <paramref name="hidden"/>: what its calibration gate (2-1) shows, from both parties' figures,
    /// that gate's 에스더 plan and its HP at <paramref name="difficulty"/>; else the user's.</summary>
    public static double Share(IReadOnlyList<RaidGate> gates, RaidGate hidden, string? difficulty, RaidSettings r)
    {
        var row = r.Gates.GetValueOrDefault(hidden.Id);
        var from = RaidData.Find(gates, hidden.CalibrateFrom);
        return from is not null && row is not null && difficulty is not null && from.Hp.TryGetValue(difficulty, out long max)
            && CalibratedShare(max, row.Assist1, row.Assist2, Esther.Total(r.Gates.GetValueOrDefault(from.Id)?.Esthers ?? [])
                * (from.Hp.TryGetValue(Esther.Basis, out long basis) && basis > 0 ? max / (double)basis : 1)) is { } cal
            && cal is >= RaidSettings.MinAssistShare and <= RaidSettings.MaxAssistShare ? cal : r.AssistShare;
    }

    /// <summary>The second's step: starts, moves or ends (null) the estimate. <paramref name="raid"/> null keeps the last one.</summary>
    public static HiddenHpState? Step(HiddenHpState? s, IReadOnlyList<RaidGate> gates, MeterStatus status, long? raid, HiddenHpOptions o, TimeSpan now)
    {
        if (s is not null && (status.Clock.Fight != s.Fight || status.HpAt > s.StartedAt)) return null;   // a new record, or a bar reads again
        s ??= Start(gates, status, now);
        if (s is null || raid is not { } r) return s;
        long b = s.Base ?? r;
        // The plan's damage is an HP share: written at EX 나이트메어, scaled to this difficulty by the gate's HP.
        double scale = s.Gate.HiddenHp.TryGetValue(Esther.Basis, out long basis) && basis > 0 ? s.Max / (double)basis : 1;
        IReadOnlyList<EstherUse> plan = o.Esthers.Count > 0 ? [.. o.Esthers.Select(u => u with { Damage = u.Damage * scale })]
            : RaidData.Find(gates, s.Gate.CalibrateFrom)?.Hp.GetValueOrDefault(s.Difficulty) is { } from
              && EstherShare(from, o.Assist1, o.Assist2, o.AssistShare) is { } share and > 0 ? [new EstherUse("2-1 결과로 추정", share * s.Max / 1e8, FallbackAt)] : [];
        long tenth = (long)(s.Max * AfterGimmick);
        // Worked out afresh each second (a use added mid-fight counts at once): the damage, the drop, then each use whose % is
        // reached; a use at 10% or under (히든 아만) waits for the gimmick, then comes off the 10% (and may take it under 0).
        long Left(long at, long drop, bool done)
        {
            long left = s.Max - (at - b) - drop;
            foreach (var u in plan.Where(u => u.Damage > 0 && (done || u.At > AfterGimmick * 100)).OrderByDescending(u => u.At))
                if (left * 100.0 / s.Max <= u.At) left -= (long)Math.Round(u.Damage * 1e8);
            return left;
        }
        var (phase, drop) = (s.Phase, s.Drop);
        var (last, changed) = s.Base is null ? (r, now) : (s.LastRaid, s.LastChange);
        bool moved = r != last;
        if (phase == GimmickPhase.None && Left(r, 0, false) <= s.Max * Gimmick) phase = GimmickPhase.Waiting;
        else if (phase == GimmickPhase.Waiting && !moved && now - changed >= PauseFor) phase = GimmickPhase.Paused;
        else if (phase == GimmickPhase.Paused && moved)
            (drop, phase) = (Left(last, 0, false) - tenth, GimmickPhase.Done);   // damage again after the revive: 10% as of before it
        if (moved) (last, changed) = (r, now);
        return s with { Base = b, Left = Left(r, drop, phase == GimmickPhase.Done), Drop = drop, Phase = phase, LastRaid = last, LastChange = changed };
    }

    /// <summary>A calibration gate (2-1) cleared (its exact HP under 10,000): each sending support's 조력 피해량 (억) for the ??? gate
    /// that calibrates from it, by party.</summary>
    public static IReadOnlyList<(string Gate, int Party, double Assist)> Calibration(IReadOnlyList<RaidGate> gates, MeterStatus status, IReadOnlyList<MeterSample> all)
    {
        if (status.Boss is not { Max: { } max, Last: < ClearedUnder } || RaidData.ByHp(gates, max) is not var (from, _)) return [];
        if (gates.FirstOrDefault(g => g.CalibrateFrom == from.Id) is not { } target) return [];
        return [.. all.Where(s => s.Role == MeterRole.서포터 && s.Party is 1 or 2 && s.Damage > 0).GroupBy(s => s.Party)
            .Select(p => (target.Id, p.Key, p.First().Damage / 1e8))];
    }

    const long ClearedUnder = 10_000;

    static HiddenHpState? Start(IReadOnlyList<RaidGate> gates, MeterStatus status, TimeSpan now)
    {
        if (status.Boss is not { Max: { } max, Last: { } last } || last > max * NearZero) return null;
        if (status.Clock.Frozen || now - status.HpAt < Unread) return null;
        if (RaidData.ByHp(gates, max) is not var (before, difficulty)) return null;
        var next = gates.FirstOrDefault(g => g.After == before.Id && g.HiddenHp.ContainsKey(difficulty));
        return next is null ? null : new(next, difficulty, next.HiddenHp[difficulty], next.HiddenHp[difficulty], status.Clock.Fight, now, null, 0);
    }
}
