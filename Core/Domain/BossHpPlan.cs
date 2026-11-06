using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>보스 체력 as a read of its own: the boss bar's text, 2/s, for what needs it without the DPS 미터기 (with the meter on, its
/// own step reads it).</summary>
public static class BossHpPlan
{
    /// <summary>쉽게 보기, or a 그림자 raid with its 기믹 체력 on, or <paramref name="homework"/> (숙제's fallback, which needs the bar
    /// with 전투 기록 or the meter off), unless the read is switched off.</summary>
    public static bool Wanted(RaidSettings? s, IReadOnlyList<RaidGate> gates, bool homework = false) =>
        s is { BossHpRead: true } && (homework || s.BossHpEasy || gates.Any(g => g.IsShadow && s.ShadowHpOn(g.Raid)));
}
