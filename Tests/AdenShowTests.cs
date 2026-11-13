using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 아덴1 / 아덴2 boxes' show / hide per profile · class, part of 클래스 전용 (class-profiles.plan.md B).</summary>
public class AdenShowTests
{
    const string Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세";
    static readonly IReadOnlySet<LookGroup> NoLooks = new HashSet<LookGroup>();

    static Settings Playing()
    {
        var s = new Settings();
        s.Characters[Lone] = new() { Class = "워로드", Subclass = "고독한기사" };
        s.ActiveCharacter = Lone;
        s.SyncClass();
        ClassProfiles.Edit(s, Lone);   // 개별: the 직업각인's own boxes
        return s;
    }

    static void HideZ(Settings s, string key, bool hidden = true)
    {
        var mine = s.Classes[key].Mine;
        if (hidden) mine.HiddenSlots.Add(SlotId.아덴1); else mine.HiddenSlots.Remove(SlotId.아덴1);
    }

    static bool ZHidden(Settings s, string key) => s.Classes[key].Mine.HiddenSlots.Contains(SlotId.아덴1);

    static CopyRequest Same(int from, int to, bool classOnly = true) => new(CopyMode.ClassToClass, from, Lone, to, Lone, NoLooks, classOnly);

    [Fact]
    public void Upgrade_022_file_shows_the_same_boxes_in_every_profile()
    {
        var s = Playing();
        HideZ(s, Lone);
        foreach (var p in s.Profiles) p.Aden = null;   // a 0.22 file has none
        var loaded = SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(s, SettingsStore.Json), SettingsStore.Json)!);

        Assert.All(loaded.Profiles, p => Assert.True(p.Aden![Lone].Hide1));
        Assert.True(ZHidden(loaded, Lone));
    }

    [Fact]
    public void Profile_switch_swaps_the_boxes()
    {
        var s = Playing();
        HideZ(s, Lone);

        s.SwitchProfile(1);
        Assert.False(ZHidden(s, Lone));
        s.SwitchProfile(0);

        Assert.True(ZHidden(s, Lone));
    }

    [Fact]
    public void ClassOnly_copy_brings_the_boxes_and_undo_puts_them_back()
    {
        var s = Playing();
        HideZ(s, Lone);
        s.SwitchProfile(1);
        s.SwitchProfile(0);
        var undo = s.Copy(Same(0, 1))!;

        Assert.True(s.Profiles[1].Aden![Lone].Hide1);
        s.SwitchProfile(1);
        Assert.True(ZHidden(s, Lone));
        s.UndoCopy(undo);

        Assert.False(ZHidden(s, Lone));   // profile 1 is live: shown at once
        Assert.False(s.Profiles[1].Aden!.ContainsKey(Lone));
    }

    [Fact]
    public void Copy_without_ClassOnly_or_between_other_classes_leaves_them()
    {
        var s = Playing();
        HideZ(s, Lone);
        s.Copy(Same(0, 1, classOnly: false) with { Looks = new HashSet<LookGroup> { LookGroup.판 } });
        s.Copy(new CopyRequest(CopyMode.ClassToClass, 0, Lone, 1, Combat, new HashSet<LookGroup> { LookGroup.판 }, true));

        Assert.False(s.Profiles[1].Aden!.ContainsKey(Lone));
    }

    [Fact]
    public void A_class_that_is_not_개별_shares_기본_s_boxes()
    {
        var s = new Settings();
        s.Characters[Combat] = new() { Class = "워로드", Subclass = "전투태세" };

        Assert.Equal(ClassProfiles.Template, ClassProfiles.ShownKey(s, Combat));
        ClassProfiles.Edit(s, Combat);
        Assert.Equal(Combat, ClassProfiles.ShownKey(s, Combat));
    }
}
