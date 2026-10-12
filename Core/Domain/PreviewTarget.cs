using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>What my window shows while 설정 has another 직업각인 than the character's picked in 편집 중: that 직업각인's
/// sample (<paramref name="AdenSlots"/>, <paramref name="Role"/>; <paramref name="ClassId"/> picks the gauge's rule), drawn with the settings of <paramref name="Key"/>
/// ("클래스 · 직업각인") instead of the live data.</summary>
public sealed record PreviewTarget(string Key, string Class, string Subclass, IReadOnlyList<SlotId> AdenSlots, Role Role, int ClassId = 0)
{
    /// <summary>The target for the picked entry, or null for the live data: the entry is the active character's (or 기본, or
    /// not a listed 직업각인).</summary>
    public static PreviewTarget? Decide(string pickedKey, string? activeKey, IEnumerable<ClassInfo> classes)
    {
        if (pickedKey == ClassProfiles.Template || pickedKey == activeKey) return null;
        foreach (var c in classes)
            foreach (var sub in c.Subclasses.Keys)
                if (Settings.CharacterKey(c.Name, sub) == pickedKey)
                    return new(pickedKey, c.Name, sub, c.AdenSlotsOf(sub), c.RoleOf(sub), c.Id);
        return null;
    }

    /// <summary>The picked 직업각인 shows icons (<paramref name="mine"/>) but none are saved for it (<paramref name="saved"/>,
    /// <c>IconStore.Load</c>): its boxes are drawn in colour and 설정 says to read the icons in game.</summary>
    public static bool IconsMissing(MineProfile mine, IReadOnlyDictionary<SlotId, Image> saved) => mine.Icons.Count > 0 && saved.Count == 0;
}
