using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>What a 스킬창 다시 맞추기 capture makes active (PLAN §7 item 6): only a character the capture confirms, else
/// 기본 (no class). Detection runs only when the user asks for it. The class's 직업각인 is the one last played
/// (Settings.SubclassHistory) when there is one; a class whose 직업각인 have the same skills takes its first without one.</summary>
public static class CharacterDetect
{
    /// <summary>Sets and returns the active character (null: 기본).</summary>
    /// <param name="learned">Characters whose learned emblem matches the capture, with the score.</param>
    /// <param name="fits">Whether the skill bar's centre piece allows that class (it shows no other one).</param>
    /// <param name="shown">The class the centre piece clearly shows, or null.</param>
    /// <param name="emblem">The capture, learned for a character added or picked here that has none.</param>
    public static string? Resolve(Settings s, IReadOnlyList<ClassInfo> classes, IReadOnlyList<(string Key, double Score)> learned,
        Func<string, bool> fits, string? shown, string emblem)
    {
        // An emblem learned while another class was set (the bar matches, the centre piece disagrees) is forgotten, so it
        // can't win again.
        var ok = new List<(string Key, double Score)>();
        foreach (var m in learned)
        {
            if (fits(s.Characters[m.Key].Class)) ok.Add(m);
            else s.Characters[m.Key].EmblemBgra = null;
        }
        if (ok.Count > 0)
            // Several characters of one class share an emblem: keep the active one if it is among them.
            return Remembered(s, classes, s.ActiveCharacter = ok.FirstOrDefault(m => m.Key == s.ActiveCharacter).Key ?? ok.MaxBy(m => m.Score).Key);

        if (shown is not null && classes.FirstOrDefault(c => c.Name == shown) is { } cls)
        {
            string key = s.ActiveCharacter is { } a && s.Characters.GetValueOrDefault(a)?.Class == shown ? a
                : s.Characters.FirstOrDefault(c => c.Value.Class == shown).Key
                  ?? s.PickCharacter(cls.Family, cls.Name, cls.Subclasses.Keys.First());   // the 초각스 read corrects the 직업각인
            s.Characters[key].EmblemBgra ??= emblem;
            return Remembered(s, classes, s.ActiveCharacter = key);
        }
        return s.ActiveCharacter = null;
    }

    /// <summary>The character of <paramref name="key"/>'s class for its 직업각인 in the history (added if new, with the emblem),
    /// or for its first when its 직업각인 have the same skills and it has none yet (remembered); else <paramref name="key"/>.</summary>
    static string Remembered(Settings s, IReadOnlyList<ClassInfo> classes, string key)
    {
        var c = s.Characters[key];
        if (classes.FirstOrDefault(i => i.Name == c.Class) is not { } cls) return key;
        string? sub = cls.Remembered(s.SubclassHistory) ?? (cls.SameSkills ? cls.Subclasses.Keys.First() : null);
        if (sub is null) return key;
        string next = s.PickCharacter(cls.Family, cls.Name, sub, remember: true);
        s.Characters[next].EmblemBgra ??= c.EmblemBgra;
        return next;
    }
}
