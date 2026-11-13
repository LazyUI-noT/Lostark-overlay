using System.Text.Json;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>복사 and 되돌리기 (<see cref="ProfileCopy"/>).</summary>
public sealed partial class Settings
{
    static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, SettingsStore.Json);
    static T? Read<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, SettingsStore.Json);

    static string GaugeKeyOf(string classKey)
    {
        var parts = classKey.Split(" · ", 2);
        return GaugeBar.Key(parts[0], parts.Length > 1 ? parts[1] : "");
    }

    /// <summary>Runs a 복사. Null when it can't run or has nothing to copy; else what <see cref="UndoCopy"/> needs. The live
    /// values are kept first and put on again when the target is the active profile. Window spots and sizes are never copied
    /// by ① ②; the target's screenshot always stays.</summary>
    public CopyUndo? Copy(CopyRequest r)
    {
        if (!ProfileCopy.Valid(r, Profiles.Count)) return null;
        foreach (var i in new[] { r.FromProfile, r.ToProfile }.Distinct().Where(i => i == ActiveProfile)) KeepLive(Profiles[i]);
        var (from, to) = (Profiles[r.FromProfile], Profiles[r.ToProfile]);
        if (r.Mode == CopyMode.WholeProfile)
        {
            var whole = new CopyUndo(r.ToProfile, null, Bytes(to), null);
            return CopyProfile(r.FromProfile, r.ToProfile) ? whole : null;
        }
        var undo = r.Mode == CopyMode.AllClasses ? CopyToAllClasses(r, to) : CopyToClass(r, from, to);
        if (r.ToProfile == ActiveProfile) PutOn(to);
        return undo;
    }

    static CopyUndo CopyToAllClasses(CopyRequest r, OverlayProfile to)
    {
        var undo = new CopyUndo(r.ToProfile, null, Bytes(to), null);
        var source = to.EntryOf(ProfileCopy.Normalize(r.FromClass));
        foreach (var e in to.Entries().Where(e => e != source)) TakeLooks(e, source, r.Looks);
        return undo;
    }

    CopyUndo CopyToClass(CopyRequest r, OverlayProfile from, OverlayProfile to)
    {
        var (fromClass, toClass) = (ProfileCopy.Normalize(r.FromClass), ProfileCopy.Normalize(r.ToClass));
        var s = this;
        var source = from.EntryOf(fromClass);
        var classOnly = r.ClassOnly && ProfileCopy.ClassOnlyAllowed(r);
        // 버프 막대 is one per profile: it reaches every class of the target, so 되돌리기 puts the profile's buff fields back
        // (only those: other edits made after the copy stay).
        var buffs = r.Looks.Contains(LookGroup.버프막대);
        var raid = r.Looks.Contains(LookGroup.줄수창);   // the 줄 수 창 likewise (user, 2026-11-14)
        var hide = r.Looks.Contains(LookGroup.디버프);   // 디버프 숨김 likewise (user, 2026-11-14)
        var wideBefore = buffs || raid || hide ? Bytes<ProfileEntry>(to) : null;
        to.Classes ??= [];
        to.GaugeBars ??= [];
        CopyUndo undo;
        ProfileEntry target;
        if (toClass is null) (undo, target) = (new(r.ToProfile, ProfileCopy.SharedKey, Bytes<ProfileEntry>(to), null), to);   // only the shared entry
        else
        {
            var existing = to.Classes.GetValueOrDefault(toClass);
            var gauge = to.GaugeBars.GetValueOrDefault(GaugeKeyOf(toClass));
            to.Aden ??= [];
            var aden = to.Aden.GetValueOrDefault(ClassProfiles.ShownKey(s, toClass));
            undo = new(r.ToProfile, toClass, Bytes<ProfileEntry?>(existing), classOnly ? Bytes<GaugeBarSettings?>(gauge) : null, classOnly ? Bytes<AdenShow?>(aden) : null);
            target = existing ?? OverlayProfile.CloneEntry(to);
        }
        TakeLooks(target, source, r.Looks);
        if (toClass is not null) to.PutEntry(toClass, target);
        if (buffs && target != to) to.TakeBuffs(target);
        if (raid && target != to) to.TakeRaid(target);
        if (hide && target != to) to.TakeDebuffHide(target);
        if (buffs || raid || hide) to.ShareProfileWide();
        if (classOnly)
        {
            var bars = from.GaugeBars?.GetValueOrDefault(GaugeKeyOf(fromClass!));
            if (bars is null) to.GaugeBars.Remove(GaugeKeyOf(toClass!));
            else to.GaugeBars[GaugeKeyOf(toClass!)] = Read<GaugeBarSettings>(Bytes(bars))!;
            var shown = ClassProfiles.ShownKey(s, toClass!);
            if (from.Aden?.GetValueOrDefault(ClassProfiles.ShownKey(s, fromClass!)) is { } adenShow) to.Aden![shown] = adenShow;
            else to.Aden!.Remove(shown);
        }
        return undo with { BuffsBefore = buffs ? wideBefore : null, RaidBefore = raid ? wideBefore : null, DebuffHideBefore = hide ? wideBefore : null };
    }

    /// <summary>The chosen looks of <paramref name="source"/> into <paramref name="target"/> (via a scratch <see cref="Settings"/>,
    /// as <see cref="ProfileEntry.TakeLook"/> reads one); 암수 and 단심 keep the target's own window spot.</summary>
    static void TakeLooks(ProfileEntry target, ProfileEntry source, IEnumerable<LookGroup> looks)
    {
        var scratch = new Settings();
        source.ApplyLooks(scratch);
        foreach (var look in looks) TakeLookKeepingSpot(target, look, scratch);
    }

    static void TakeLookKeepingSpot(ProfileEntry e, LookGroup look, Settings from)
    {
        // 암수 and 단심 hold their window's spot and size: the entry's own stays.
        var (amsu, judgment) = (e.Amsu is { } a ? WindowPlace.Of(a) : null, e.Judgment is { } j ? WindowPlace.Of(j) : null);
        e.TakeLook(look, from);
        if (amsu is not null) amsu.To(e.Amsu!);
        if (judgment is not null) judgment.To(e.Judgment!);
    }

    /// <summary>Puts back what a <see cref="Copy"/> overwrote (its target profile or class only; the screenshot stays) and
    /// shows it at once when that profile is the active one. False when the profile is gone.</summary>
    public bool UndoCopy(CopyUndo u)
    {
        if (u.Profile < 0 || u.Profile >= Profiles.Count) return false;
        var p = Profiles[u.Profile];
        if (u.ClassKey is not null && u.Profile == ActiveProfile) KeepLive(p);
        // 버프 막대, the 줄 수 창 and 디버프 숨김 are one per profile: an undo that didn't copy them keeps today's (edits after the copy stay).
        var wideNow = u.ClassKey is null ? null : Read<ProfileEntry>(Bytes<ProfileEntry>(p));
        if (u.ClassKey == ProfileCopy.SharedKey) p.RestoreShared(Read<ProfileEntry>(u.Before)!);
        else if (u.ClassKey is null)
        {
            var restored = Read<OverlayProfile>(u.Before)!;
            restored.Shot = p.Shot;
            Profiles[u.Profile] = p = restored;
        }
        else
        {
            p.Classes ??= [];
            if (Read<ProfileEntry?>(u.Before) is { } entry) p.PutEntry(u.ClassKey, entry);
            else
            {
                p.Classes.Remove(u.ClassKey);
                p.SubclassPlaces.Remove(u.ClassKey);
            }
            if (u.GaugeBefore is not null)
            {
                p.GaugeBars ??= [];
                if (Read<GaugeBarSettings?>(u.GaugeBefore) is { } bars) p.GaugeBars[GaugeKeyOf(u.ClassKey)] = bars;
                else p.GaugeBars.Remove(GaugeKeyOf(u.ClassKey));
            }
            if (u.AdenBefore is not null)
            {
                p.Aden ??= [];
                var shown = ClassProfiles.ShownKey(this, u.ClassKey);
                if (Read<AdenShow?>(u.AdenBefore) is { } aden) p.Aden[shown] = aden;
                else p.Aden.Remove(shown);
            }
        }
        if (wideNow is not null)
        {
            p.TakeBuffs(u.BuffsBefore is { } buffs ? Read<ProfileEntry>(buffs)! : wideNow);
            p.TakeRaid(u.RaidBefore is { } raid ? Read<ProfileEntry>(raid)! : wideNow);
            p.TakeDebuffHide(u.DebuffHideBefore is { } hide ? Read<ProfileEntry>(hide)! : wideNow);
        }
        p.ShareProfileWide();
        if (u.Profile == ActiveProfile) PutOn(p);
        return true;
    }
}
