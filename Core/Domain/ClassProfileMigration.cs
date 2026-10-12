using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>Every shipped 직업각인 has settings of its own, as if it were a class: <see cref="EnsureOwn"/> gives each one an
/// own entry holding what it shows, so the 설정 can edit any of them. Run once after the settings are loaded (the class
/// list comes from classes.json, which <see cref="SettingsStore"/> doesn't read).</summary>
public static class ClassProfileMigration
{
    /// <summary>Makes the entry of every 직업각인 in <paramref name="classes"/> exist and own, holding what
    /// <see cref="ClassProfiles.Of"/> returns for it: its own values when it already is own (kept as they are), else a copy
    /// of what it followed (its class's, else 기본's; anything it kept from an earlier 개별 is not what it showed). The class
    /// entries, 기본 and entries of classes or 직업각인 not in the list stay as they are. Window spots saved per 직업각인
    /// carry over (<see cref="SeedPlaces"/>). Idempotent; true when something was added or changed.</summary>
    public static bool EnsureOwn(Settings s, IEnumerable<ClassInfo> classes)
    {
        List<string> added = [];
        foreach (var c in classes)
            foreach (var sub in c.Subclasses.Keys)
            {
                var key = Settings.CharacterKey(c.Name, sub);
                if (ClassProfiles.IsOwn(s, key)) continue;
                var entry = ClassProfiles.Entry(s, key);
                entry.Mine = ClassProfiles.Clone(ClassProfiles.Of(s, ClassProfiles.Parent(key)).Mine);
                entry.Own = true;
                added.Add(key);
            }
        SeedPlaces(s, added);
        return added.Count > 0;
    }

    /// <summary>A 직업각인 that followed its class stood at the class's spot and size. Where a 프로필 holds spots per 직업각인
    /// (so the class's differs from the one now on screen), each newly own 직업각인 takes the class's, or it would start
    /// where the window happens to be. A 프로필 with one spot for all needs nothing: a 직업각인 placed for the first time
    /// starts where the window is (<see cref="Settings.SyncPlace"/>).</summary>
    static void SeedPlaces(Settings s, List<string> added)
    {
        if (added.Count == 0) return;
        if (s.ActiveProfile >= 0 && s.ActiveProfile < s.Profiles.Count) s.Profiles[s.ActiveProfile].KeepPlace(s.PlaceKey, s);   // the window's spot, as SyncPlace would store it
        foreach (var profile in s.Profiles.Where(p => p.SubclassPlaces.Count > 0))
            foreach (var key in added.Where(k => !profile.SubclassPlaces.ContainsKey(k)))
                profile.SubclassPlaces[key] = WindowPlace.Of(profile.Overlay);
    }
}
