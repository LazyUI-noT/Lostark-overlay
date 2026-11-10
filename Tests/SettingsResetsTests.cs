using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>Each reset in 설정 clears exactly what its label names.</summary>
public class SettingsResetsTests
{
    [Fact]
    public void The_display_reset_keeps_the_overlay_place_size_and_plate_and_leaves_the_buffs()
    {
        var s = new Settings();
        (s.Overlay.Opacity, s.Overlay.GoldThickness, s.Overlay.ShowTimer) = (0.5, 5, false);
        (s.Overlay.Left, s.Overlay.Top, s.Overlay.Width, s.Overlay.Height, s.Overlay.PlateColor) = (10, 20, 1.5, 0.8, "#111111");
        s.Buffs.Size = 80;

        SettingsResets.Display(s);

        var d = new OverlaySettings();
        Assert.Equal((d.Opacity, d.GoldThickness, d.ShowTimer), (s.Overlay.Opacity, s.Overlay.GoldThickness, s.Overlay.ShowTimer));
        Assert.Equal((10.0, 20.0, 1.5, 0.8, "#111111"), (s.Overlay.Left, s.Overlay.Top, s.Overlay.Width, s.Overlay.Height, s.Overlay.PlateColor));
        Assert.Equal(80, s.Buffs.Size);
    }

    [Fact]
    public void The_plate_is_shown_by_default_and_the_display_reset_shows_it_again()
    {
        var s = new Settings();
        Assert.True(s.Overlay.ShowPlate);
        s.Overlay.ShowPlate = false;

        SettingsResets.Display(s);

        Assert.True(s.Overlay.ShowPlate);
    }

    [Fact]
    public void The_party_reset_keeps_the_party_windows_and_the_display_name()
    {
        var s = new Settings();
        var p = s.Party;
        (p.ShowOthersBySlot[1].Left, p.DisplayName, p.Together, p.Left, p.PlateColor) = (300, "백", true, 40, "#222222");
        (p.SendGauge, p.IconScale) = (false, 1.5);

        SettingsResets.Party(s);

        p = s.Party;
        Assert.Equal((300.0, "백", true, 40.0, "#222222"), (p.ShowOthersBySlot[1].Left, p.DisplayName, p.Together, p.Left, p.PlateColor));
        Assert.Equal((true, 1.0), (p.SendGauge, p.IconScale));
    }

    [Fact]
    public void The_class_reset_keeps_개별()
    {
        var s = new Settings();
        var c = ClassProfiles.Edit(s, "바드");
        c.Mine.Gauge = false;

        SettingsResets.Classes(s);

        var after = s.Classes["바드"];
        Assert.True(after.Mine.Gauge);
        Assert.True(after.Own);
    }

    [Fact]
    public void The_gauge_reset_clears_every_bar()
    {
        var s = new Settings();
        s.Gauge.Thickness = 12;
        GaugeOverlay.Core.Data.GaugeBar.Edit(s, "바드", "진실된 용맹").GlowEnabled = false;

        SettingsResets.Gauge(s);

        Assert.Equal(new GaugeStyle().Thickness, s.Gauge.Thickness);
        Assert.Empty(s.GaugeBars);
    }

    [Fact]
    public void Resetting_everything_keeps_the_characters_and_overlay_profiles()
    {
        var s = new Settings();
        string key = s.PickCharacter("전사", "워로드", "전투태세");
        s.Fits["2560x1440"] = new Fit(100);
        s.Keys[SlotId.일반1] = "Z";
        s.Hotkeys.EditMode = "F9";
        s.Profiles[2].Overlay.Left = 77;

        SettingsResets.All(s);

        Assert.Equal(key, s.ActiveCharacter);
        Assert.True(s.Characters.ContainsKey(key));
        Assert.Empty(s.Fits);
        Assert.Equal(new Settings().Keys[SlotId.일반1], s.Keys[SlotId.일반1]);
        Assert.Equal(new HotkeySettings().EditMode, s.Hotkeys.EditMode);
        Assert.Equal(77, s.Profiles[2].Overlay.Left);
    }

    [Fact]
    public void Resetting_everything_keeps_the_buff_debuff_and_단심_windows_and_what_단심_is_on_for()
    {
        var s = new Settings();
        (s.Buffs.Left, s.Buffs.Top, s.Buffs.Width, s.Buffs.Size) = (5, 6, 1.2, 80);
        s.Buffs.Edit(BuffKind.공증).Visible = false;
        (s.Debuffs.Left, s.Debuffs.Width, s.Debuffs.Raid) = (7, 1.4, Raid.모르둠);
        s.Debuffs.Box(2).Height = 2;
        s.Judgment.Set("워로드 · 고독한 기사", true);
        (s.Judgment.FontSize, s.Judgment.Left, s.Judgment.Opacity) = (200, 50, 0.5);

        SettingsResets.All(s);

        Assert.Equal((5.0, 6.0, 1.2, 120), (s.Buffs.Left, s.Buffs.Top, s.Buffs.Width, s.Buffs.Size));
        Assert.Empty(s.Buffs.Indicators);
        Assert.Equal((7.0, 1.4, Raid.모르둠), (s.Debuffs.Left, s.Debuffs.Width, s.Debuffs.Raid));
        Assert.Empty(s.Debuffs.Boxes);
        Assert.Equal((true, 200, 50.0, 1.0), (s.Judgment.On("워로드 · 고독한 기사"), s.Judgment.FontSize, s.Judgment.Left, s.Judgment.Opacity));
    }
}
