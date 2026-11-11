using System.Text.Json;

namespace GaugeOverlay.Core.Domain;

/// <summary>오버레이 프로필 1–5 and the window spot kept per 직업각인.</summary>
public sealed partial class Settings
{
    /// <summary>After the character changed: when the settings your boxes use (<see cref="ClassProfiles.ActiveKey"/>)
    /// are a 직업각인's, or no longer are, keeps your window's spot and size for the one left (in the active profile) and
    /// puts back the new one's; a 직업각인 placed for the first time starts where the window is. True when it changed.</summary>
    public bool SyncPlace()
    {
        var key = ClassProfiles.ActiveKey(this) is { } k && ClassProfiles.Parent(k) is { } p && p != ClassProfiles.Template ? k : null;
        if (key == PlaceKey) return false;
        if (ActiveProfile >= 0 && ActiveProfile < Profiles.Count)
        {
            var profile = Profiles[ActiveProfile];
            bool placed = key is null || profile.SubclassPlaces.ContainsKey(key);
            profile.KeepPlace(PlaceKey, this);
            if (placed) profile.PlaceOf(key).To(Overlay);
        }
        PlaceKey = key;
        return true;
    }

    /// <summary>Keeps the current places in the active profile and puts back <paramref name="index"/>'s. The same or a
    /// missing profile changes nothing.</summary>
    public void SwitchProfile(int index)
    {
        if (index == ActiveProfile || index < 0 || index >= Profiles.Count) return;
        if (ActiveProfile >= 0 && ActiveProfile < Profiles.Count) Profiles[ActiveProfile].CaptureFrom(this);
        Profiles[index].ApplyTo(this);
        ActiveProfile = index;
    }

    /// <summary>The 모든 배치에 적용 mark of <paramref name="look"/>. On: the live (active 배치's) value is the shared one.
    /// Off: every 배치 starts from it, then keeps its own.</summary>
    public void ShareLook(LookGroup look, bool shared)
    {
        SharedLooks ??= [];
        if (shared) { SharedLooks.Add(look); return; }
        if (!SharedLooks.Remove(look)) return;
        foreach (var p in Profiles) p.TakeLook(look, this);
    }

    /// <summary>The looks where another 배치 holds a value unlike the live (active) one.</summary>
    internal List<LookGroup> DifferingLooks()
    {
        var live = new OverlayProfile();
        return [.. Enum.GetValues<LookGroup>().Where(g =>
        {
            live.TakeLook(g, this);
            var key = live.LookKey(g);
            return Profiles.Where((_, i) => i != ActiveProfile).Any(p => p.LookKey(g) != key);
        })];
    }

    /// <summary>The one-time notice after an older file's 배치 were merged (<see cref="MergedLooks"/>); null when none differed.</summary>
    public string? MergedLooksNotice() => MergedLooks.Count == 0 ? null
        : $"오버레이 배치마다 달랐던 설정을 지금 배치({ActiveProfile + 1})의 값으로 맞췄습니다: {string.Join(", ", MergedLooks)}.\n\n"
          + "이제 이 설정들은 모든 배치에 같게 적용됩니다. 배치마다 따로 두려면 설정에서 그 항목의 '모든 배치에 적용'을 끄세요.";

    /// <summary>배치 복사: the active profile, as it is now, into <paramref name="to"/>, all but the screenshot, which
    /// <paramref name="to"/> keeps. The active or a missing profile changes nothing (false).</summary>
    public bool CopyProfile(int to)
    {
        if (to == ActiveProfile || to < 0 || to >= Profiles.Count || ActiveProfile < 0 || ActiveProfile >= Profiles.Count) return false;
        var from = Profiles[ActiveProfile];
        from.CaptureFrom(this);
        var copy = JsonSerializer.Deserialize<OverlayProfile>(JsonSerializer.SerializeToUtf8Bytes(from, Data.SettingsStore.Json), Data.SettingsStore.Json)!;
        copy.Shot = Profiles[to].Shot;
        Profiles[to] = copy;
        return true;
    }
}
