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
}
