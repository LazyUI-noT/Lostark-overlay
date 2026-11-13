using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>버프 막대 (합쳐서 / 따로, 세로, its window, bar sizes and look) is one per 프로필, the same for every class in it
/// (user, 2026-11-13); the rest of a profile stays per class.</summary>
public class BuffPerProfileTests
{
    const string Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세";

    static Settings Playing(string key)
    {
        var s = SettingsStore.Normalize(new Settings());   // as loaded: every profile's looks filled
        s.Characters[Lone] = new() { Class = "워로드", Subclass = "고독한기사" };
        s.Characters[Combat] = new() { Class = "워로드", Subclass = "전투태세" };
        Go(s, key);
        return s;
    }

    static void Go(Settings s, string key)
    {
        s.ActiveCharacter = key;
        s.SyncClass();
    }

    static void SetBuffs(Settings s, bool apart, double left, string color)
    {
        (s.Buffs.Detached, s.Buffs.Left, s.Buffs.Drain) = (apart, left, !apart);
        s.Buffs.Edit(BuffKind.공증).Color = color;
        s.Buffs.Edit(BuffKind.공증).Length = 0.5;
    }

    static (bool, double, bool, string, double) Buffs(Settings s) =>
        (s.Buffs.Detached, s.Buffs.Left, s.Buffs.Drain, s.Buffs.Of(BuffKind.공증).Color, s.Buffs.Of(BuffKind.공증).Length);

    [Fact]
    public void Buff_settings_are_shared_by_every_class_in_a_profile()
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;   // Lone becomes its own entry
        SetBuffs(s, apart: true, left: 40, color: "#123456");
        Go(s, Combat);
        s.Overlay.Left = 500;   // Combat too

        Assert.Equal((true, 40.0, false, "#123456", 0.5), Buffs(s));
        SetBuffs(s, apart: false, left: 70, color: "#654321");
        Go(s, Lone);

        Assert.Equal((false, 70.0, true, "#654321", 0.5), Buffs(s));
        Assert.Equal(100, s.Overlay.Left);   // the skill window's spot is still the class's
    }

    [Fact]
    public void Another_profile_keeps_its_own_buff_settings()
    {
        var s = Playing(Lone);
        SetBuffs(s, apart: true, left: 40, color: "#123456");
        s.SwitchProfile(1);
        SetBuffs(s, apart: false, left: 70, color: "#654321");

        s.SwitchProfile(0);
        Assert.Equal((true, 40.0, false, "#123456", 0.5), Buffs(s));
    }

    [Fact]
    public void Copying_the_buff_look_into_a_class_reaches_the_whole_target_profile()
    {
        var s = Playing(Lone);
        s.Buffs.Edit(BuffKind.공증).Color = "#AAAAAA";
        s.SwitchProfile(1);
        s.Overlay.Left = 300;   // profile 2 has a Lone entry
        Go(s, Combat);
        s.Overlay.Left = 600;   // and a Combat one
        var looks = new HashSet<LookGroup> { LookGroup.버프막대 };

        var undo = s.Copy(new(CopyMode.ClassToClass, 0, Lone, 1, Lone, looks, false));
        Assert.NotNull(undo);
        Assert.Equal("#AAAAAA", s.Buffs.Of(BuffKind.공증).Color);   // live Combat, profile 2
        Assert.All(s.Profiles[1].Entries(), e => Assert.Equal("#AAAAAA", e.BuffLook!.Bars[BuffKind.공증].Color));

        Assert.True(s.UndoCopy(undo!));
        Assert.NotEqual("#AAAAAA", s.Buffs.Of(BuffKind.공증).Color);
        Assert.All(s.Profiles[1].Entries(), e => Assert.False(e.BuffLook?.Bars.ContainsKey(BuffKind.공증) == true && e.BuffLook.Bars[BuffKind.공증].Color == "#AAAAAA"));
    }

    [Fact]
    public void A_file_with_buffs_per_class_loads_with_the_profiles_shared_ones()
    {
        var s = new Settings();
        var p = s.Profiles[0];
        p.BuffsDetached = true;
        var own = OverlayProfile.Clone(p);
        own.BuffsDetached = false;
        p.Classes = new() { [Lone] = own };
        p.GaugeBars = [];
        p.Aden = [];

        p.Migrate(s);
        Assert.True(p.Classes[Lone].BuffsDetached);
    }
}
