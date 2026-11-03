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

    public static RaidRecognitionState Update(RaidRecognitionState s, string? matched, TimeSpan now)
    {
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
        GateSettings? Row(RaidGate g) => s.Gates.GetValueOrDefault(g.Id);
        bool Count(GateSettings r) => s.CountEnabled && r.CountWindow;   // the main 줄 수 switch gates every gate's own
        bool debuffAuto = s.DebuffsAuto && gates.Any(g => g.DebuffRaid != Raid.없음);   // 디버프 창 자동 waits for a gate with debuffs
        if (!debuffAuto && !gates.Any(g => Row(g) is { } r && Count(r))) return (false, false, null);
        bool readName = s.Auto && (debuffAuto || gates.Any(g => Row(g) is { } r && Count(r) && r.CountAuto));
        // 자동: the recognised gate while its 줄 수 창 is on, else the gate whose 줄 수 창 shows without waiting.
        var seen = RaidData.Find(gates, recognised);
        var fixedGate = gates.FirstOrDefault(g => Row(g) is { CountAuto: false } r && Count(r));
        var gate = !s.Auto ? RaidData.Find(gates, s.ManualGate)
            : seen is not null && Row(seen) is { } sr && Count(sr) ? seen : fixedGate ?? seen;
        return (readName, gate is not null && Row(gate) is { } gr && Count(gr), gate);
    }

    /// <summary>The debuff window's raid (user, 2026-11-04): the raid picked in the tray (<paramref name="picked"/>), else with
    /// 디버프 창 자동 the recognised gate's (or the gate picked by hand), else 없음; never outside a dungeon (it showed in 지옥).</summary>
    public static Raid DebuffRaid(RaidSettings s, IReadOnlyList<RaidGate> gates, string? recognised, bool inDungeon, Raid picked) =>
        !inDungeon ? Raid.없음
        : picked != Raid.없음 ? picked
        : s.DebuffsAuto && RaidData.Find(gates, s.Auto ? recognised : s.ManualGate) is { } g ? g.DebuffRaid
        : Raid.없음;
}
