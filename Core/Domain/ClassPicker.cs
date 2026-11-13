using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 편집 중's list: 기본 and every shipped 직업각인, never a class alone (a class's own entry is only
/// the party members' look, edited with its 직업각인).</summary>
public static class ClassPicker
{
    /// <summary>기본 first, then "클래스 · 직업각인" for each 직업각인 in classes.json order.</summary>
    public static IReadOnlyList<string> PickerEntries(IEnumerable<ClassInfo> classes) =>
        [ClassProfiles.Template, .. classes.SelectMany(c => c.Subclasses.Keys.Select(sub => Settings.CharacterKey(c.Name, sub)))];

    /// <summary><paramref name="entries"/> with the ★ ones first (a class name stars all its 직업각인), each group in its
    /// own order. Favourites not in the list change nothing.</summary>
    public static IReadOnlyList<string> FavoritesFirst(IReadOnlyList<string> entries, IReadOnlyCollection<string> favorites) =>
        [.. entries.Where(e => Starred(e, favorites)), .. entries.Where(e => !Starred(e, favorites))];

    /// <summary>An entry ("클래스 · 직업각인", or a class alone) is ★: starred itself, or its class is; 기본 never.</summary>
    public static bool Starred(string entry, IReadOnlyCollection<string> favorites) =>
        entry != ClassProfiles.Template && (favorites.Contains(entry) || (ClassProfiles.Parent(entry) is { } p && p != ClassProfiles.Template && favorites.Contains(p)));

    /// <summary>편집 중 typed into (user, 2026-11-12): <paramref name="shown"/> (an entry's text, "클래스 · 직업각인") has every
    /// word of <paramref name="typed"/>, case and spaces ignored, so the class or the 직업각인 alone finds it.</summary>
    public static bool Matches(string shown, string typed)
    {
        string Squeeze(string t) => string.Concat(t.Where(ch => !char.IsWhiteSpace(ch)));
        var text = Squeeze(shown);
        return typed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The entry to show for <paramref name="shown"/> (null = nothing picked yet): itself while it is in
    /// <paramref name="entries"/> (<see cref="PickerEntries"/>), else, for an old class-alone or unknown key, the active
    /// character's entry (<see cref="ClassProfiles.ActiveKey"/>) when listed, else 기본.</summary>
    public static string Normalize(string? shown, string? activeKey, IEnumerable<string> entries)
    {
        var listed = entries as ICollection<string> ?? [.. entries];
        if (shown is not null && listed.Contains(shown)) return shown;
        return activeKey is not null && listed.Contains(activeKey) ? activeKey : ClassProfiles.Template;
    }

    /// <summary>복사 → 대상's ★ list: <paramref name="favorites"/>, plus the 직업각인 of each character with one assigned
    /// (user, 2026-11-11), each once.</summary>
    public static IReadOnlyList<string> CopyFavorites(IEnumerable<string> favorites, IReadOnlyDictionary<string, CharacterSettings> characters) =>
        [.. favorites.Concat(characters.Values.Where(c => c.Class != "" && c.Subclass != "").Select(c => Settings.CharacterKey(c.Class, c.Subclass))).Distinct()];

    /// <summary>The entries 복사 → 대상 writes into: <paramref name="target"/> (null = 모든 클래스: 기본 and every 직업각인) as a
    /// settings entry. Party members' settings are the class's, so for <paramref name="toMembers"/> a 직업각인 stands for its
    /// class; each entry once.</summary>
    public static IReadOnlyList<string> CopyTargets(string? target, IEnumerable<ClassInfo> classes, bool toMembers)
    {
        var picked = target is null ? PickerEntries(classes) : [target];
        return [.. picked.Select(k => toMembers && ClassProfiles.Parent(k) is { } p and not ClassProfiles.Template ? p : k).Distinct()];
    }

    const CopyParts CooldownViewParts = CopyParts.Progress | CopyParts.Timer;   // 나 → 나 only (ClassProfiles.Copy)

    /// <summary>Why 복사 → 대상 can't run, or null when it can: nothing ticked, only 쿨 진행 표시 / 남은 쿨 (초) with a party
    /// side, or the settings copied onto themselves (<paramref name="from"/> and <paramref name="target"/> the same entry on the
    /// same side, where party members' entry is the class's).</summary>
    public static string? CopyRefusal(CopyParts parts, string from, bool fromMembers, string? target, bool toMembers, IEnumerable<ClassInfo> classes)
    {
        if (parts == 0) return "복사할 것을 고르세요.";
        if ((fromMembers || toMembers) && (parts & ~CooldownViewParts) == 0) return "쿨 진행 표시와 남은 쿨 (초)는 나에서 나로만 복사됩니다.";
        if (target is null || fromMembers != toMembers) return null;
        string source = fromMembers && ClassProfiles.Parent(from) is { } p and not ClassProfiles.Template ? p : from;
        return CopyTargets(target, classes, toMembers) is [var only] && only == source
            ? "같은 설정에는 복사할 수 없습니다. 가져올 설정이나 대상을 바꾸세요." : null;
    }

    /// <summary>복사 → 모든 직업각인 overwrites everything: it takes a second click (a single 직업각인 doesn't).</summary>
    public static bool CopyNeedsConfirm(string? target) => target is null;

    /// <summary>파티원's line when a 직업각인 is picked: its class's party look is shared by all of the class's 직업각인; null
    /// when nobody shares it (one or no 직업각인, or no class).</summary>
    public static string? SharedPartyNote(ClassInfo? cls) => cls?.Subclasses.Count switch
    {
        null or < 2 => null,
        2 => "두 직업각인이 같이 씁니다",
        var n => $"{n}개 직업각인이 같이 씁니다",
    };
}
