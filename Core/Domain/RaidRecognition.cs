using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>The gate recognised from the boss's name (<see cref="Gate"/>, null: none), a different read waiting for a second to
/// agree, and when the recognised gate was last read.</summary>
public sealed record RaidRecognitionState(string? Gate, string? Pending, TimeSpan SeenAt)
{
    public static readonly RaidRecognitionState None = new(null, null, TimeSpan.Zero);
}

/// <summary>설정 → 레이드 자동 인식: a gate is recognised once two name reads in a row agree, and kept through misses (a cutscene,
/// a cast effect over the name) for <see cref="Keep"/>. Pure.</summary>
public static class RaidRecognition
{
    public static readonly TimeSpan Keep = TimeSpan.FromSeconds(10);

    /// <param name="daenantu">대난투 is up: it shows no boss name (user, 2026-11-09), so a miss then keeps the gate as a read would.</param>
    public static RaidRecognitionState Update(RaidRecognitionState s, string? matched, TimeSpan now, bool daenantu = false)
    {
        if (matched is null && daenantu && s.Gate is not null) return s with { Pending = null, SeenAt = now };
        if (matched is null) return s.Gate is not null && now - s.SeenAt > Keep ? RaidRecognitionState.None : s with { Pending = null };
        if (matched == s.Gate) return s with { Pending = null, SeenAt = now };
        return s.Pending == matched ? new(matched, null, now) : s with { Pending = matched };
    }
}

/// <summary>What the engine reads for 설정 → 레이드 this frame: the boss's name (only while a window on 자동 waits for it), the
/// bar count (the gate's 줄 수 창 is on), and the gate whose alerts apply: picked by hand, recognised, or the one gate whose
/// 줄 수 창 shows without waiting. Nothing when no window is on. Pure.</summary>
public static class RaidReadPlan
{
    public static (bool ReadName, bool ReadCount, RaidGate? Gate) Of(RaidSettings s, IReadOnlyList<RaidGate> gates, string? recognised)
    {
        GateSettings Row(RaidGate g) => s.Gates.GetValueOrDefault(g.Id) ?? Defaults;   // a gate without a row has the defaults
        bool count = s.CountEnabled;   // the main 줄 수 switch turns every gate on
        bool debuffAuto = s.DebuffsAuto && gates.Any(g => g.DebuffRaid != Raid.없음);   // 디버프 창 자동 waits for a gate with debuffs
        if (!debuffAuto && !(count && gates.Count > 0)) return (false, false, null);
        bool readName = s.Auto && (debuffAuto || (count && gates.Any(g => Row(g).CountAuto)));
        // 자동: the recognised gate, else the gate whose 줄 수 창 shows without waiting.
        var seen = RaidData.Find(gates, recognised);
        var fixedGate = count ? gates.FirstOrDefault(g => !Row(g).CountAuto) : null;
        var gate = !s.Auto ? RaidData.Find(gates, s.ManualGate)
            : count && seen is not null ? seen : fixedGate ?? seen;
        return (readName, count && gate is not null, gate);
    }

    static readonly GateSettings Defaults = new();

    /// <summary>The debuff window's raid (user, 2026-11-03): the raid picked in the tray (<paramref name="picked"/>), else with
    /// 디버프 창 자동 the recognised gate's (or the gate picked by hand), else 없음; never outside a dungeon (it showed in 지옥).</summary>
    public static Raid DebuffRaid(RaidSettings s, IReadOnlyList<RaidGate> gates, string? recognised, bool inDungeon, Raid picked) =>
        !inDungeon ? Raid.없음
        : picked != Raid.없음 ? picked
        : s.DebuffsAuto && RaidData.Find(gates, s.Auto ? recognised : s.ManualGate) is { } g ? g.DebuffRaid
        : Raid.없음;
}
