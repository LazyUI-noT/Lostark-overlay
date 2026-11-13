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
    /// entries, 기본 and entries of classes or 직업각인 not in the list stay as they are. Window spots and looks are per
    /// 직업각인 whether 개별 or not (<see cref="Settings.SyncClass"/>), so nothing to carry over. Idempotent; true when
    /// something was added or changed.</summary>
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
        return added.Count > 0;
    }

    /// <summary>An older file's global 쿨 진행 표시 (style, direction) and 남은 쿨 (초) go into 기본 and every 개별 entry, so
    /// nothing looks different (display-split.plan.md step 5); also every entry whose 개별 is off but kept, as it comes back
    /// when turned on. Bare entries stay bare: they show what they follow, and <see cref="ClassProfiles.SetOwn"/> copies
    /// into them only while bare.</summary>
    internal static void MoveCooldownViews(Settings s)
    {
        var o = s.Overlay;
        ClassProfiles.Entry(s, ClassProfiles.Template);
        foreach (var (key, c) in s.Classes)
            if (key == ClassProfiles.Template || c is { Own: true } || !ClassProfiles.IsBare(c))
                (c.Mine.CooldownStyle, c.Mine.FillFrom, c.Mine.ShowTimer) = (o.CooldownStyle, o.FillFrom, o.ShowTimer);
        s.CooldownLooksMoved = true;
    }
}
