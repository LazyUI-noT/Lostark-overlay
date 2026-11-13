namespace GaugeOverlay.Core.Domain;

/// <summary>The 아덴스킬 group and the 아덴1 / 아덴2 (Z / C) boxes' show / hide of one <see cref="ClassSettings"/> entry, kept per
/// profile (<see cref="OverlayProfile.Aden"/>) as part of 클래스 전용 (class-profiles.plan.md B; the 아덴 게이지's own on / off stays
/// in <see cref="ClassProfile.Gauge"/>). Only the entries that differ from the default are stored.</summary>
public sealed record AdenShow(bool Group = true, bool Hide1 = false, bool Hide2 = false)
{
    public static readonly AdenShow Default = new();

    public static AdenShow Of(ClassProfile p) =>
        new(p.Groups.GetValueOrDefault(SlotGroup.아덴스킬, true), p.HiddenSlots.Contains(SlotId.아덴1), p.HiddenSlots.Contains(SlotId.아덴2));

    public void To(ClassProfile p)
    {
        p.Groups[SlotGroup.아덴스킬] = Group;
        foreach (var (slot, hidden) in new[] { (SlotId.아덴1, Hide1), (SlotId.아덴2, Hide2) })
            if (hidden) p.HiddenSlots.Add(slot); else p.HiddenSlots.Remove(slot);
    }

    /// <summary>What every entry of <paramref name="classes"/> shows now, the default ones left out.</summary>
    public static Dictionary<string, AdenShow> Snapshot(IReadOnlyDictionary<string, ClassSettings> classes) =>
        classes.Where(c => c.Value is not null && Of(c.Value.Mine) != Default).ToDictionary(c => c.Key, c => Of(c.Value.Mine));

    /// <summary>Puts <paramref name="shown"/> on the entries of <paramref name="classes"/> (the default where it has none).</summary>
    public static void Apply(IReadOnlyDictionary<string, ClassSettings> classes, IReadOnlyDictionary<string, AdenShow> shown)
    {
        foreach (var (key, c) in classes)
            if (c is not null) shown.GetValueOrDefault(key, Default).To(c.Mine);
    }
}
