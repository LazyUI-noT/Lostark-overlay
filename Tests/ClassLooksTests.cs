using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Inside each 프로필, looks and spots per 클래스 · 직업각인 (class-profiles.plan.md A, phase 2).</summary>
public class ClassLooksTests
{
    const string Warlord = "워로드", Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세";

    static Settings Playing(string? key)
    {
        var s = new Settings();
        s.Characters[Lone] = new() { Class = Warlord, Subclass = "고독한기사" };
        s.Characters[Combat] = new() { Class = Warlord, Subclass = "전투태세" };
        Go(s, key);
        return s;
    }

    static void Go(Settings s, string? key)
    {
        s.ActiveCharacter = key;
        s.SyncClass();
    }

    static OverlayProfile Active(Settings s) => s.Profiles[s.ActiveProfile];

    [Fact]
    public void Look_key_is_the_played_직업각인_개별_or_not()
    {
        var s = Playing(Lone);
        Assert.False(ClassProfiles.IsOwn(s, Lone));
        Assert.Equal(Lone, ClassProfiles.LookKey(s));

        s.Characters["바드 · "] = new() { Class = "바드", Subclass = "" };
        s.ActiveCharacter = "바드 · ";
        Assert.Null(ClassProfiles.LookKey(s));   // 직업각인 unknown: the shared look
        s.ActiveCharacter = null;
        Assert.Null(ClassProfiles.LookKey(s));
    }

    [Fact]
    public void Switching_subclass_swaps_looks_and_spots()
    {
        var s = Playing(Lone);
        (s.Overlay.Left, s.Overlay.Opacity, s.Amsu.Color) = (100, 0.5, "#111111");
        Go(s, Combat);
        (s.Overlay.Left, s.Overlay.Opacity, s.Amsu.Color) = (500, 0.7, "#222222");

        Go(s, Lone);
        Assert.Equal((100.0, 0.5, "#111111"), (s.Overlay.Left, s.Overlay.Opacity, s.Amsu.Color));
        Go(s, Combat);
        Assert.Equal((500.0, 0.7, "#222222"), (s.Overlay.Left, s.Overlay.Opacity, s.Amsu.Color));
    }

    [Fact]
    public void Class_not_set_up_draws_shared()
    {
        var s = Playing(null);
        (s.Overlay.Left, s.Overlay.Opacity) = (300, 0.6);   // no class known: the shared look
        Go(s, Lone);
        Assert.Equal((300.0, 0.6), (s.Overlay.Left, s.Overlay.Opacity));
        s.Overlay.Opacity = 0.2;
        Go(s, Combat);   // never set up: the shared look and spot, not Lone's

        Assert.Equal((300.0, 0.6), (s.Overlay.Left, s.Overlay.Opacity));
    }

    [Fact]
    public void Unchanged_class_keeps_no_entry()
    {
        var s = Playing(null);
        s.Overlay.Left = 300;
        Go(s, Lone);
        Go(s, Combat);
        Go(s, null);

        Assert.Empty(Active(s).Classes!);
        Assert.Empty(Active(s).SubclassPlaces);
    }

    [Fact]
    public void Changed_class_becomes_own()
    {
        var s = Playing(Lone);
        s.Gauge.Fill = "#ABCDEF";
        Go(s, Combat);

        var entry = Assert.Contains(Lone, Active(s).Classes!);
        Assert.Equal("#ABCDEF", entry.GaugeLook!.Fill);
        Assert.NotEqual("#ABCDEF", Active(s).GaugeLook?.Fill);   // the shared look is untouched
        Assert.Equal(entry.Overlay.Left, Active(s).SubclassPlaces[Lone].Left, 6);   // the mirror 0.22 reads
    }

    [Fact]
    public void A_class_back_to_the_shared_look_drops_its_entry()
    {
        var s = Playing(Lone);
        var before = s.Overlay.Opacity;
        s.Overlay.Opacity = 0.3;
        Go(s, Combat);
        Go(s, Lone);
        s.Overlay.Opacity = before;
        Go(s, Combat);

        Assert.DoesNotContain(Lone, Active(s).Classes!.Keys);
        Assert.DoesNotContain(Lone, Active(s).SubclassPlaces.Keys);
    }

    [Fact]
    public void Each_profile_keeps_its_own_class_entries()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        s.SwitchProfile(1);
        Assert.Equal(0.9, s.Overlay.Opacity);   // profile 2's Lone was never set up
        s.Overlay.Opacity = 0.4;
        s.SwitchProfile(0);

        Assert.Equal(0.3, s.Overlay.Opacity);
        s.SwitchProfile(1);
        Assert.Equal(0.4, s.Overlay.Opacity);
    }

    [Fact]
    public void Party_windows_stay_one_per_profile()
    {
        var s = Playing(Lone);
        s.Party.Left = 42;
        Go(s, Combat);

        Assert.Equal(42, s.Party.Left);
    }

    [Fact]
    public void Profile_switch_swaps_gauge_bars()
    {
        var s = Playing(Lone);
        GaugeBar.Edit(s, Warlord, "고독한기사").GlowEnabled = false;

        s.SwitchProfile(1);
        Assert.False(s.GaugeBars.ContainsKey(GaugeBar.Key(Warlord, "고독한기사")));
        GaugeBar.Edit(s, Warlord, "고독한기사").SplitThickness = 3;
        s.SwitchProfile(0);

        var bar = s.GaugeBars[GaugeBar.Key(Warlord, "고독한기사")];
        Assert.Equal((false, new GaugeBarSettings().SplitThickness), (bar.GlowEnabled, bar.SplitThickness));
        s.SwitchProfile(1);
        Assert.Equal(3, s.GaugeBars[GaugeBar.Key(Warlord, "고독한기사")].SplitThickness);
    }

    [Fact]
    public void Undo_across_class_change_lands_on_live_class()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.5;
        var snapshot = SettingsStore.Copy(s);   // 설정's step before a change, taken while on Lone
        s.Overlay.Opacity = 0.4;
        Go(s, Combat);                         // the game switched 직업각인 meanwhile

        s.CopyFrom(snapshot);

        Assert.Equal(Combat, s.PlaceKey);
        Assert.Equal(0.9, s.Overlay.Opacity);   // Combat's (the shared) look on screen
        Go(s, Lone);
        Assert.Equal(0.5, s.Overlay.Opacity);   // the undone value landed on Lone
    }

    [Fact]
    public void Copying_a_profile_brings_its_class_entries_and_gauge_bars()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        GaugeBar.Edit(s, Warlord, "고독한기사").SplitThickness = 4;

        Assert.True(s.CopyProfile(2));
        s.SwitchProfile(2);

        Assert.Equal((0.3, 4.0), (s.Overlay.Opacity, s.GaugeBars[GaugeBar.Key(Warlord, "고독한기사")].SplitThickness));
    }

    [Fact]
    public void A_look_applied_to_all_reaches_every_class_entry()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        Go(s, Combat);
        s.Amsu.Color = "#ABCDEF";

        s.ApplyLookToAll(LookGroup.암수);
        Go(s, Lone);

        Assert.Equal(("#ABCDEF", 0.3), (s.Amsu.Color, s.Overlay.Opacity));
    }

    [Fact]
    public void Class_entries_round_trip_through_the_file()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        Go(s, Combat);

        var back = SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, SettingsStore.Json), SettingsStore.Json)!);
        Go(back, Lone);

        Assert.Equal(0.3, back.Overlay.Opacity);
    }
}
