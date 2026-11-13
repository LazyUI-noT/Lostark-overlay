using System.Text.Json;
using System.Text.Json.Nodes;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 줄 수 창's settings (켜기, 빛나기, 빛날 때만 표시, the glow rules, 배경 색, 불투명도) are one per 프로필, the same for
/// every class in it (user, 2026-11-14): 빛날 때만 표시 ticked on one class showed the window all the time on another.</summary>
public class RaidPerProfileTests
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

    [Fact]
    public void Only_when_glowing_follows_every_class_in_a_profile()
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;   // Lone becomes its own entry
        Go(s, Combat);
        s.Overlay.Left = 500;   // Combat too
        (s.Raids.OnlyWhenGlowing, s.Raids.GlowSeconds, s.Raids.Background) = (true, 7, "#CC112233");

        Go(s, Lone);

        Assert.Equal((true, 7, "#CC112233"), (s.Raids.OnlyWhenGlowing, s.Raids.GlowSeconds, s.Raids.Background));
        Assert.Equal(100, s.Overlay.Left);   // the skill window's spot is still the class's
    }

    [Fact]
    public void Another_profile_keeps_its_own()
    {
        var s = Playing(Lone);
        s.Raids.OnlyWhenGlowing = true;
        s.SwitchProfile(1);

        Assert.False(s.Raids.OnlyWhenGlowing);
        s.SwitchProfile(0);
        Assert.True(s.Raids.OnlyWhenGlowing);
    }

    [Fact]
    public void Saved_file_with_one_class_ticked_opens_with_the_played_class_value_for_all()
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;
        Go(s, Combat);
        s.Overlay.Left = 500;
        Go(s, Lone);   // keeps Combat's entry
        // A 0.23 file: the tick stored for Combat only, the played Lone without it.
        s.Profiles[0].Classes![Combat].RaidShow = new RaidShow(OnlyWhenGlowing: true);
        Assert.False(s.Raids.OnlyWhenGlowing);

        var back = SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, SettingsStore.Json), SettingsStore.Json)!);
        Go(back, Combat);

        Assert.False(back.Raids.OnlyWhenGlowing);   // one value for the profile now: the shared one, not Combat's old tick
        Assert.All(back.Profiles[0].Classes!.Values, e => Assert.Equal(back.Profiles[0].RaidShow, e.RaidShow));
    }

    [Fact]
    public void Copy_to_one_class_reaches_the_whole_profile_and_undoes()
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;
        Go(s, Combat);
        s.Overlay.Left = 500;
        s.SwitchProfile(1);
        s.Raids.OnlyWhenGlowing = true;
        s.SwitchProfile(0);

        var undo = s.Copy(new CopyRequest(CopyMode.ClassToClass, 1, Combat, 0, Combat, new HashSet<LookGroup> { LookGroup.줄수창 }, ClassOnly: false));

        Assert.True(s.Raids.OnlyWhenGlowing);
        Go(s, Lone);
        Assert.True(s.Raids.OnlyWhenGlowing);
        Assert.True(s.UndoCopy(undo!));
        Assert.False(s.Raids.OnlyWhenGlowing);
        Go(s, Combat);
        Assert.False(s.Raids.OnlyWhenGlowing);
    }

    [Fact]
    public void Copying_debuff_hide_to_one_class_reaches_the_whole_profile_and_undoes()
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;
        Go(s, Combat);
        s.Overlay.Left = 500;
        s.SwitchProfile(1);
        s.Debuffs.HideWhenEmpty = true;
        s.SwitchProfile(0);

        var undo = s.Copy(new CopyRequest(CopyMode.ClassToClass, 1, Combat, 0, Combat, new HashSet<LookGroup> { LookGroup.디버프 }, ClassOnly: false));

        Assert.True(s.Debuffs.HideWhenEmpty);
        Go(s, Lone);
        Assert.True(s.Debuffs.HideWhenEmpty);
        Assert.True(s.UndoCopy(undo!));
        Assert.False(s.Debuffs.HideWhenEmpty);
        Go(s, Combat);
        Assert.False(s.Debuffs.HideWhenEmpty);
    }

    [Theory]
    [InlineData(Combat)]   // into one class
    [InlineData(null)]     // into 기본, the shared entry
    public void Undo_of_another_look_keeps_a_later_count_window_edit(string? toClass)
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;
        Go(s, Combat);
        s.Overlay.Left = 500;
        Go(s, Lone);
        Go(s, Combat);   // playing the class the 복사 goes into

        var undo = s.Copy(new CopyRequest(CopyMode.ClassToClass, 1, Lone, 0, toClass, new HashSet<LookGroup> { LookGroup.판 }, ClassOnly: false));
        s.Raids.OnlyWhenGlowing = true;   // after the copy
        Assert.True(s.UndoCopy(undo!));

        Assert.True(s.Raids.OnlyWhenGlowing);
        Assert.True(s.Profiles[0].RaidShow!.OnlyWhenGlowing);
        Assert.All(s.Profiles[0].Classes!.Values, e => Assert.True(e.RaidShow!.OnlyWhenGlowing));
    }

    static Settings TwoClassesInProfile0()
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;
        Go(s, Combat);
        s.Overlay.Left = 500;
        Go(s, Lone);
        return s;
    }

    static void AllAgree(OverlayProfile p, bool onlyWhenGlowing) =>
        Assert.All(p.Entries(), e => Assert.Equal(onlyWhenGlowing, e.RaidShow!.OnlyWhenGlowing));

    [Fact]
    public void Copy_into_the_shared_entry_reaches_every_class_and_undoes()
    {
        var s = TwoClassesInProfile0();
        s.SwitchProfile(1);
        s.Raids.OnlyWhenGlowing = true;
        s.SwitchProfile(0);

        var undo = s.Copy(new CopyRequest(CopyMode.ClassToClass, 1, Lone, 0, null, new HashSet<LookGroup> { LookGroup.줄수창 }, ClassOnly: false));

        Assert.True(s.Raids.OnlyWhenGlowing);
        AllAgree(s.Profiles[0], true);
        Assert.True(s.UndoCopy(undo!));
        Assert.False(s.Raids.OnlyWhenGlowing);
        AllAgree(s.Profiles[0], false);
    }

    [Fact]
    public void Copy_to_all_classes_leaves_every_class_on_the_profiles_value()
    {
        var s = TwoClassesInProfile0();
        s.Raids.OnlyWhenGlowing = true;

        var undo = s.Copy(new CopyRequest(CopyMode.AllClasses, 0, Lone, 0, null, new HashSet<LookGroup> { LookGroup.줄수창 }, ClassOnly: false));

        AllAgree(s.Profiles[0], true);
        Go(s, Combat);
        Assert.True(s.Raids.OnlyWhenGlowing);
        Assert.True(s.UndoCopy(undo!));
        Assert.True(s.Raids.OnlyWhenGlowing);   // the 복사 changed nothing: one value already
    }

    [Fact]
    public void Undoing_a_copy_of_both_puts_back_both()
    {
        var s = TwoClassesInProfile0();
        s.SwitchProfile(1);
        (s.Raids.OnlyWhenGlowing, s.Buffs.Edit(BuffKind.공증).Color) = (true, "#AAAAAA");
        s.SwitchProfile(0);
        var buffColor = s.Buffs.Of(BuffKind.공증).Color;

        var undo = s.Copy(new CopyRequest(CopyMode.ClassToClass, 1, Lone, 0, Combat,
            new HashSet<LookGroup> { LookGroup.버프막대, LookGroup.줄수창 }, ClassOnly: false));
        Assert.Equal((true, "#AAAAAA"), (s.Raids.OnlyWhenGlowing, s.Buffs.Of(BuffKind.공증).Color));
        Assert.True(s.UndoCopy(undo!));

        Assert.Equal((false, buffColor), (s.Raids.OnlyWhenGlowing, s.Buffs.Of(BuffKind.공증).Color));
        AllAgree(s.Profiles[0], false);
        Go(s, Combat);
        Assert.Equal((false, buffColor), (s.Raids.OnlyWhenGlowing, s.Buffs.Of(BuffKind.공증).Color));
    }

    [Theory]
    [InlineData("RaidShow")]
    [InlineData("RaidLook")]
    public void Hand_edited_nulls_load_as_defaults_shared_by_every_class(string field)
    {
        var s = TwoClassesInProfile0();
        var json = JsonNode.Parse(JsonSerializer.Serialize(s, SettingsStore.Json))!;
        var profile = json["Profiles"]![0]!;
        profile[field] = null;
        foreach (var (_, entry) in profile["Classes"]!.AsObject()) entry![field] = null;

        var back = SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(json.ToJsonString(), SettingsStore.Json)!);

        var p = back.Profiles[0];
        Assert.All(p.Entries(), e => Assert.Equal((p.RaidShow, p.RaidLook), (e.RaidShow, e.RaidLook)));
        Assert.NotNull(p.RaidShow);
        Assert.NotNull(p.RaidLook);
        Assert.True(p.RaidShow!.CountEnabled);   // a null never reads as the count turned off
    }
}
