using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 편집 중's list: 기본 and every shipped 직업각인, never a class alone (a class's own entry is only
/// the party members' look, edited with its 직업각인).</summary>
public static class ClassPicker
{
    /// <summary>기본 first, then "클래스 · 직업각인" for each 직업각인 in classes.json order.</summary>
    public static IReadOnlyList<string> PickerEntries(IEnumerable<ClassInfo> classes) =>
        [ClassProfiles.Template, .. classes.SelectMany(c => c.Subclasses.Keys.Select(sub => Settings.CharacterKey(c.Name, sub)))];

    /// <summary>The entry to show for <paramref name="shown"/> (null = nothing picked yet): itself while it is in
    /// <paramref name="entries"/> (<see cref="PickerEntries"/>), else, for an old class-alone or unknown key, the active
    /// character's entry (<see cref="ClassProfiles.ActiveKey"/>) when listed, else 기본.</summary>
    public static string Normalize(string? shown, string? activeKey, IEnumerable<string> entries)
    {
        var listed = entries as ICollection<string> ?? [.. entries];
        if (shown is not null && listed.Contains(shown)) return shown;
        return activeKey is not null && listed.Contains(activeKey) ? activeKey : ClassProfiles.Template;
    }

    /// <summary>The entries 복사 → 대상 writes into: <paramref name="target"/> (null = 모든 클래스: 기본 and every 직업각인) as a
    /// settings entry. Party members' settings are the class's, so for <paramref name="toMembers"/> a 직업각인 stands for its
    /// class; each entry once.</summary>
    public static IReadOnlyList<string> CopyTargets(string? target, IEnumerable<ClassInfo> classes, bool toMembers)
    {
        var picked = target is null ? PickerEntries(classes) : [target];
        return [.. picked.Select(k => toMembers && ClassProfiles.Parent(k) is { } p and not ClassProfiles.Template ? p : k).Distinct()];
    }

    /// <summary>파티원's line when a 직업각인 is picked: its class's party look is shared by all of the class's 직업각인; null
    /// when nobody shares it (one or no 직업각인, or no class).</summary>
    public static string? SharedPartyNote(ClassInfo? cls) => cls?.Subclasses.Count switch
    {
        null or < 2 => null,
        2 => "두 직업각인이 같이 씁니다",
        var n => $"{n}개 직업각인이 같이 씁니다",
    };
}
