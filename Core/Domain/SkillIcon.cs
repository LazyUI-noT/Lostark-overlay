namespace GaugeOverlay.Core.Domain;

/// <summary>아이콘으로 표시: when a slot's icon is copied for the overlay (Engine.IconSeen), and which slots a 직업각인's saved
/// icons (IconStore) must cover. Icons are copied only while a copy run is on: 아이콘 다시 읽기, or saved icons missing.</summary>
public static class SkillIcon
{
    /// <summary>A ready read of a slot not copied yet in this run (<paramref name="sent"/> false). 아덴 skills are never copied.</summary>
    public static bool Due(SlotId slot, Reading reading, bool sent, IReadOnlyCollection<SlotId> adenSlots) =>
        reading.Kind == ReadKind.Ready && !sent && SlotInfo.GroupOf(slot, adenSlots) != SlotGroup.아덴스킬;

    /// <summary>The slots drawn as icons: in a group with 아이콘으로 표시 on (<paramref name="iconGroups"/>), the group shown
    /// (<paramref name="groups"/>, shown when absent), the slot not hidden, and not a 아덴 skill.</summary>
    public static IReadOnlySet<SlotId> Needed(IReadOnlyCollection<SlotGroup> iconGroups, IReadOnlyDictionary<SlotGroup, bool> groups,
        IReadOnlySet<SlotId> hidden, IReadOnlyCollection<SlotId> adenSlots) =>
        SlotInfo.All.Where(slot => SlotInfo.GroupOf(slot, adenSlots) is var g && g != SlotGroup.아덴스킬 && iconGroups.Contains(g)
            && groups.GetValueOrDefault(g, true) && !hidden.Contains(slot)).ToHashSet();

    /// <summary>Every needed slot has a saved icon: no copy run is needed.</summary>
    public static bool Complete(IReadOnlySet<SlotId> saved, IEnumerable<SlotId> needed) => needed.All(saved.Contains);
}
