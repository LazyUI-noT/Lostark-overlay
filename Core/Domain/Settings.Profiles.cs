using System.Text.Json;

namespace GaugeOverlay.Core.Domain;

/// <summary>프로필 1–5, and inside each the places and looks per 클래스 · 직업각인 (class-profiles.plan.md).</summary>
public sealed partial class Settings
{
    /// <summary>After the character changed: when the played 클래스 · 직업각인 (<see cref="ClassProfiles.LookKey"/>) is
    /// another than the one the live values hold (<see cref="PlaceKey"/>), keeps them as that one's in the active profile and
    /// puts the new one's on (the profile's shared ones where it has none). True when it changed.</summary>
    public bool SyncClass()
    {
        var key = ClassProfiles.LookKey(this);
        if (key == PlaceKey) return false;
        if (ActiveProfile >= 0 && ActiveProfile < Profiles.Count)
        {
            var profile = Profiles[ActiveProfile];
            profile.Keep(PlaceKey, this);
            profile.EntryOf(key).ApplyTo(this);
        }
        PlaceKey = key;
        return true;
    }

    /// <summary>Keeps the current places, looks and 게이지 bars in the active profile and puts back <paramref name="index"/>'s
    /// for the played class. The same or a missing profile changes nothing.</summary>
    public void SwitchProfile(int index)
    {
        if (index == ActiveProfile || index < 0 || index >= Profiles.Count) return;
        if (ActiveProfile >= 0 && ActiveProfile < Profiles.Count) KeepLive(Profiles[ActiveProfile]);
        PutOn(Profiles[index]);
        ActiveProfile = index;
    }

    void KeepLive(OverlayProfile p)
    {
        p.Keep(PlaceKey, this);
        p.GaugeBars = OverlayProfile.CloneBars(GaugeBars);
        p.Aden = AdenShow.Snapshot(Classes);
    }

    void PutOn(OverlayProfile p)
    {
        p.Apply(PlaceKey, this);
        GaugeBars = OverlayProfile.CloneBars(p.GaugeBars ?? GaugeBars);   // none (a hand-built Settings): the live ones stay
        if (p.Aden is not null) AdenShow.Apply(Classes, p.Aden);
    }

    /// <summary>모든 프로필에 적용: <paramref name="look"/>'s live value into every profile and every class in it, once (user,
    /// 2026-11-13: a button, not a shared mark). Each keeps its own afterwards; window spots and sizes aren't touched.</summary>
    public void ApplyLookToAll(LookGroup look)
    {
        foreach (var e in Profiles.SelectMany(p => p.Entries())) TakeLookKeepingSpot(e, look, this);
    }

    /// <summary>An older file's 모든 프로필에 적용 marks (<see cref="SharedLooks"/>): each marked look's live value goes into
    /// every profile, as it showed in all of them, and nothing stays shared (<c>SettingsStore.Normalize</c>).</summary>
    internal void UnshareLooks()
    {
        foreach (var look in SharedLooks ?? []) ApplyLookToAll(look);
        SharedLooks = [];
    }

    /// <summary>A 0.22 file (<c>SettingsStore.Normalize</c>): its active profile takes the live values as 0.22 kept them (the
    /// looks one per profile), then every profile's 직업각인 spots become class entries (<see cref="OverlayProfile.Migrate"/>).</summary>
    internal void MigrateClasses()
    {
        if (ActiveProfile >= 0 && ActiveProfile < Profiles.Count && Profiles[ActiveProfile].Classes is null)
            Profiles[ActiveProfile].Keep022(PlaceKey, this);
        foreach (var p in Profiles) p.Migrate(this);
    }

    /// <summary>프로필 복사: the active profile, as it is now, into <paramref name="to"/>, all but the screenshot, which
    /// <paramref name="to"/> keeps. The active or a missing profile changes nothing (false).</summary>
    public bool CopyProfile(int to) => CopyProfile(ActiveProfile, to);

    /// <summary>프로필 <paramref name="from"/> (as it is now, the active one included) into <paramref name="to"/>, every class's
    /// entry and 게이지 bars too, all but the screenshot, which <paramref name="to"/> keeps. Into the active one it is put on
    /// screen at once. The same or a missing profile changes nothing (false).</summary>
    public bool CopyProfile(int from, int to)
    {
        if (from == to || from < 0 || from >= Profiles.Count || to < 0 || to >= Profiles.Count) return false;
        if (from == ActiveProfile) KeepLive(Profiles[from]);
        var copy = JsonSerializer.Deserialize<OverlayProfile>(JsonSerializer.SerializeToUtf8Bytes(Profiles[from], Data.SettingsStore.Json), Data.SettingsStore.Json)!;
        copy.Shot = Profiles[to].Shot;
        Profiles[to] = copy;
        if (to == ActiveProfile) PutOn(copy);
        return true;
    }
}
