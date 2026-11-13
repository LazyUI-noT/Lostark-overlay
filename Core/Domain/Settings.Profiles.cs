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

    /// <summary>What 설정 edits for 편집 중 = <paramref name="key"/> (edit-follows-pick.plan.md): null when it is the played
    /// 직업각인 (edit the live values), else a copy of these settings holding <paramref name="key"/>'s places and looks in the
    /// active profile (기본 = <see cref="ClassProfiles.Template"/> or null: the shared ones). Hand edits back with
    /// <see cref="KeepEdit"/>.</summary>
    public Settings? EditTarget(string? key)
    {
        key = EntryKey(key);
        if (key == PlaceKey || ActiveProfile < 0 || ActiveProfile >= Profiles.Count) return null;
        var profile = Profiles[ActiveProfile];
        profile.Keep(PlaceKey, this);   // unkept live edits first, so the copy shows what is stored
        var target = Data.SettingsStore.Copy(this);
        profile.EntryOf(key).ApplyTo(target);
        // Not in an entry, so 설정 edits them on the live settings: one per profile (버프 막대, 파티원 창) or per class (칸 옵션,
        // 게이지 막대). Shared, the copy's canvas draws and drags those edits.
        (target.Buffs, target.Party, target.Classes, target.GaugeBars) = (Buffs, Party, Classes, GaugeBars);
        return target;
    }

    /// <summary>Stores <paramref name="target"/>'s places and looks as <paramref name="key"/>'s in the active profile. The
    /// party windows and 버프 막대 stay the live ones (설정 edits those on these settings). Editing 기본 while the played
    /// 직업각인 follows it puts the change on screen too.</summary>
    public void KeepEdit(string? key, Settings target)
    {
        key = EntryKey(key);
        if (ActiveProfile < 0 || ActiveProfile >= Profiles.Count) return;
        if (key == PlaceKey)
        {
            // The class became the played one since the copy was made: its edit goes on screen, not lost.
            OwnEntry(target).ApplyTo(this);
            return;
        }
        var profile = Profiles[ActiveProfile];
        profile.Keep(PlaceKey, this);
        bool liveFollows = key is null && profile.EntryOf(PlaceKey) == profile;
        profile.KeepEdit(key, target, this);
        if (liveFollows) profile.ApplyTo(this);
    }

    static string? EntryKey(string? key) => key is ClassProfiles.Template ? null : key;

    /// <summary><paramref name="target"/>'s places and looks, with the one-per-profile ones (버프 막대, 줄 수 창, 디버프 숨김)
    /// taken from these live settings, which 설정 edits.</summary>
    ProfileEntry OwnEntry(Settings target)
    {
        var (own, live) = (new ProfileEntry(), new ProfileEntry());
        own.CaptureFrom(target);
        live.CaptureFrom(this);
        own.TakeProfileWide(live);
        return own;
    }

    /// <summary>Brings what 설정 edits on these live settings (타이머 크기, 미터기 수치, 줄 수 창, 디버프 숨김 …) into
    /// <paramref name="target"/> (<see cref="EditTarget"/>), so the canvas drawing it shows them; its own places and looks
    /// stay. In place: 설정's rows hold <paramref name="target"/>'s objects (암수, 단심, 디버프 칸).</summary>
    /// <remarks>ponytail: copies only plain values of the windows' settings; a live-only list or dictionary stays as the
    /// copy had it (none is drawn from the copy today).</remarks>
    public void MirrorLive(Settings target)
    {
        var own = OwnEntry(target);
        CopyPlain(Overlay, target.Overlay);
        CopyPlain(Meter, target.Meter);
        CopyPlain(Raids, target.Raids);
        CopyPlain(Debuffs, target.Debuffs);
        CopyPlain(Gauge, target.Gauge);
        var (amsu, judgment, boxes) = (target.Amsu, target.Judgment, target.Debuffs.Boxes);
        own.ApplyTo(target);
        (target.Amsu, target.Judgment, target.Debuffs.Boxes) = (amsu, judgment, boxes);   // the same values, the rows' objects
    }

    static void CopyPlain<T>(T from, T to) where T : class
    {
        foreach (var p in typeof(T).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0
                && (p.PropertyType.IsValueType || p.PropertyType == typeof(string)))
                p.SetValue(to, p.GetValue(from));
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
