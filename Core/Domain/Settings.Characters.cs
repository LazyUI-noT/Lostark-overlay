namespace GaugeOverlay.Core.Domain;

/// <summary>Characters, 코어 빌드, learned stack skills and 직업각인 history.</summary>
public sealed partial class Settings
{
    /// <summary>Makes the "클래스 · 직업각인" character active (the key the 캐릭터 tab uses), adding it the first time; its
    /// emblem is learned at the next capture. Returns the key.
    /// <paramref name="remember"/>: it was picked by hand or read off the bar, so it goes into <see cref="SubclassHistory"/>.</summary>
    public string PickCharacter(string family, string className, string subclass, bool remember = false)
    {
        var key = CharacterKey(className, subclass);
        if (!Characters.ContainsKey(key)) Characters[key] = new() { ClassFamily = family, Class = className, Subclass = subclass };
        if (remember) SubclassHistory[className] = subclass;
        return ActiveCharacter = key;
    }

    /// <summary>The key of a character in <see cref="Characters"/>, <see cref="Cores"/>: "클래스 · 직업각인".</summary>
    public static string CharacterKey(string className, string subclass) => $"{className} · {subclass}";

    /// <summary>The choice saved for <paramref name="characterKey"/>, or the default (off, 1 1 1) when none was ever made.</summary>
    public CoreChoice CoreOf(string characterKey) => Cores.GetValueOrDefault(characterKey) ?? new();

    /// <summary>Saves <paramref name="choice"/> (digits clamped to 1–3) for <paramref name="characterKey"/> only.</summary>
    public void SetCore(string characterKey, CoreChoice choice) => Cores[characterKey] = choice.Clamped();

    public IReadOnlyList<SlotId> StackSkillsOf(string characterKey) =>
        [.. (StackSkills.GetValueOrDefault(characterKey) ?? []).Select(n => Enum.TryParse<SlotId>(n, out var s) && s.ToString() == n ? s : (SlotId?)null)
            .OfType<SlotId>().Distinct()];

    /// <summary>Remembers <paramref name="slot"/> as a stack skill of <paramref name="characterKey"/>; false if it was known.</summary>
    public bool AddStackSkill(string characterKey, SlotId slot)
    {
        var known = StackSkillsOf(characterKey);
        if (known.Contains(slot)) return false;
        StackSkills[characterKey] = [.. known.Append(slot).Select(s => s.ToString())];
        return true;
    }

    /// <summary>The 초각스 icon shows <paramref name="subclass"/>: switches to the active class's character for it (added
    /// if new), which takes the emblem when it has none, since both are the same class on screen.</summary>
    public void DetectedSubclass(string subclass)
    {
        if (ActiveCharacter is not { } k || !Characters.TryGetValue(k, out var c)) return;
        PickCharacter(c.ClassFamily, c.Class, subclass, remember: true);
        Characters[ActiveCharacter].EmblemBgra ??= c.EmblemBgra;
    }
}
