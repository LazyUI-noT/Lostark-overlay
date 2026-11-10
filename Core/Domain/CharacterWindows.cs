namespace GaugeOverlay.Core.Domain;

/// <summary>One character's hidden windows (the gold part of their switch on the 표시 map; read for everyone either way) and
/// the skill counted for them: 스킬 사용 횟수, by key slot.</summary>
public sealed class CharacterWindows
{
    public HashSet<BuffKind> Buffs { get; set; } = [];
    public bool Meter { get; set; }
    /// <summary>The skill the meter counts (null: no line).</summary>
    public SlotId? MeterSkill { get; set; }
    /// <summary>Its name on the meter; empty: the key.</summary>
    public string? MeterSkillName { get; set; }
    /// <summary>The skill the timeline rings and averages, and the 전투 기록 rows show (null: 없음). Its own, never the meter's
    /// (user, 2026-11-09).</summary>
    public SlotId? TimelineSkill { get; set; }

    /// <summary>A different skill starts on its key: the name given was the old skill's.</summary>
    public void SetMeterSkill(SlotId? slot) => (MeterSkill, MeterSkillName) = (slot, slot == MeterSkill ? MeterSkillName : null);

    /// <summary>The name the user gave <paramref name="slot"/>, if it is the meter's skill.</summary>
    public string? NameOf(SlotId slot) => slot == MeterSkill && !string.IsNullOrWhiteSpace(MeterSkillName) ? MeterSkillName.Trim() : null;
}
