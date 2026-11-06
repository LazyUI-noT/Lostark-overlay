using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>버프 막대 and DPS 미터기 창: read for everyone, shown per 클래스 · 직업각인 (the gold part of their switch, user 2026-11-05).</summary>
public class PerCharacterShowTests
{
    const string Bard = "바드 · 진실된 용기", Warlord = "워로드 · 고독한기사";

    static Settings Playing(string key)
    {
        var s = new Settings { ActiveCharacter = key };
        s.Characters[key] = new() { Class = key.Split(" · ")[0], Subclass = key.Split(" · ")[1] };
        return s;
    }

    [Fact]
    public void A_bar_hidden_for_one_character_shows_for_another_and_is_still_read()
    {
        var s = Playing(Bard);
        ClassProfiles.Edit(s, Bard);   // each 직업각인 has its own settings after start (ClassProfileMigration.EnsureOwn)
        ClassProfiles.EditWindows(s, Bard).Buffs.Add(BuffKind.공증);
        Assert.False(ClassProfiles.BuffShown(s, BuffKind.공증));
        Assert.True(ClassProfiles.BuffShown(s, BuffKind.아덴피증));
        Assert.NotNull(ClassProfiles.Reads(s, []).BuffSize);   // the row is read for the meter's 적용률

        s.ActiveCharacter = Warlord;
        s.Characters[Warlord] = new() { Class = "워로드", Subclass = "고독한기사" };
        ClassProfiles.Edit(s, Warlord);
        Assert.True(ClassProfiles.BuffShown(s, BuffKind.공증));
    }

    [Fact]
    public void A_bar_off_for_everyone_is_not_shown_anywhere()
    {
        var s = Playing(Bard);
        s.Buffs.Edit(BuffKind.공증).Visible = false;
        Assert.False(ClassProfiles.BuffShown(s, BuffKind.공증));
    }

    [Fact]
    public void The_meter_window_hides_for_one_character_only()
    {
        var s = Playing(Bard);
        s.Meter.SetMode(ThreeWay.켜기);
        ClassProfiles.Edit(s, Bard);
        Assert.True(ClassProfiles.MeterShown(s));
        ClassProfiles.EditWindows(s, Bard).Meter = true;
        Assert.False(ClassProfiles.MeterShown(s));
        Assert.True(s.Meter.On);   // still read: records and 적용률 go on
        Assert.Equal(BoxState.Hidden, FeatureMap.State(s, "meter"));
    }

    [Fact]
    public void The_tray_hides_buffs_and_meter_for_the_character_played()
    {
        var s = Playing(Bard);
        s.Meter.SetMode(ThreeWay.켜기);
        TrayToggles.Show(s, OverlayPart.Buffs, false);
        TrayToggles.Show(s, OverlayPart.Meter, false);
        Assert.False(TrayToggles.IsShown(s, OverlayPart.Buffs));
        Assert.False(TrayToggles.IsShown(s, OverlayPart.Meter));
        Assert.True(s.Buffs.Of(BuffKind.공증).Visible);   // reading goes on
        Assert.Equal(ThreeWay.켜기, s.Meter.Mode());

        TrayToggles.Show(s, OverlayPart.Buffs, true);
        TrayToggles.Show(s, OverlayPart.Meter, true);
        Assert.True(TrayToggles.IsShown(s, OverlayPart.Buffs));
        Assert.True(TrayToggles.IsShown(s, OverlayPart.Meter));
    }

    [Fact]
    public void Hiding_a_window_keeps_the_character_following_its_parent()
    {
        var s = Playing(Bard);
        TrayToggles.Show(s, OverlayPart.Buffs, false);
        TrayToggles.Show(s, OverlayPart.Meter, false);
        Assert.False(ClassProfiles.IsOwn(s, Bard));
    }

    [Fact]
    public void With_no_class_known_the_hides_are_the_templates()
    {
        var s = new Settings();
        TrayToggles.Show(s, OverlayPart.Buffs, false);
        Assert.Contains(BuffKind.공증, ClassProfiles.WindowsOf(s, null).Buffs);
        Assert.True(ClassProfiles.BuffShown(Playing(Bard), BuffKind.공증));
    }
}
