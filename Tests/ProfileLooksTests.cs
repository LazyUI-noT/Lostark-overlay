using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>암수, 단심 and the meter's buff bar 투명도 follow 프로필 1–5 (profile-looks.plan.md).</summary>
public class ProfileLooksTests
{
    static Settings Loaded(Action<Settings>? edit = null)
    {
        var s = new Settings { SharedLooks = [] };   // every look per 배치, as these tests check
        s.Amsu.Color = "#112233";
        s.Judgment.FontSize = 150;   // one of JudgmentSettings.FontSizes, or the load resets it
        s.Meter.Opacity = 0.6;
        edit?.Invoke(s);
        return SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(s, SettingsStore.Json), SettingsStore.Json)!);
    }

    [Fact]
    public void A_file_without_them_gives_every_profile_today_s_settings()
    {
        var s = Loaded();

        Assert.All(s.Profiles, p =>
        {
            Assert.Equal("#112233", p.Amsu!.Color);
            Assert.Equal(150, p.Judgment!.FontSize);
            Assert.Equal(0.6, p.MeterBarOpacity);
        });
    }

    [Fact]
    public void A_file_without_them_opens_looking_as_before()
    {
        var s = Loaded();

        Assert.Equal(("#112233", 150, 0.6), (s.Amsu.Color, s.Judgment.FontSize, s.Meter.Opacity));
        Assert.Equal(0.6, s.Meter.BarOpacity);   // the bars start at the meter window's 투명도
    }

    [Fact]
    public void Each_profile_keeps_its_own()
    {
        var s = Loaded();
        s.Amsu.On = true;
        s.Judgment.Characters.Add("가");
        s.Meter.BarOpacity = 0.3;

        s.SwitchProfile(1);
        Assert.Equal((false, 0, 0.6), (s.Amsu.On, s.Judgment.Characters.Count, s.Meter.BarOpacity));
        s.Amsu.Look = AmsuLook.원;

        s.SwitchProfile(0);
        Assert.Equal((true, AmsuLook.막대, 0.3), (s.Amsu.On, s.Amsu.Look, s.Meter.BarOpacity));
        Assert.Contains("가", s.Judgment.Characters);
        s.SwitchProfile(1);
        Assert.Equal(AmsuLook.원, s.Amsu.Look);
    }

    [Fact]
    public void A_reset_bar_opacity_doesn_t_take_the_other_profile_s()
    {
        var s = Loaded();
        s.SwitchProfile(1);
        s.Meter.BarOpacity = 0.3;
        s.SwitchProfile(0);
        s.Meter.BarOpacity = null;   // the slider's ↺: back to the meter window's 투명도

        s.SwitchProfile(1);
        s.SwitchProfile(0);

        Assert.Equal(0.6, s.Meter.BarOpacity ?? s.Meter.Opacity);
    }

    [Fact]
    public void Editing_after_a_switch_doesn_t_change_the_stored_profile()
    {
        var s = Loaded();
        s.SwitchProfile(1);
        s.Amsu.Color = "#FFFFFF";
        s.Judgment.Characters.Add("나");

        Assert.Equal("#112233", s.Profiles[1].Amsu!.Color);
        Assert.Empty(s.Profiles[1].Judgment!.Characters);
    }

    [Fact]
    public void Broken_values_in_a_profile_are_clamped()
    {
        var s = Loaded(x =>
        {
            x.Profiles[2].Amsu = new() { Opacity = 7, WarnSeconds = 99 };
            x.Profiles[2].Judgment = new() { FontSize = 3, Opacity = -1, Characters = null! };
            x.Profiles[2].MeterBarOpacity = double.NaN;
        });

        var p = s.Profiles[2];
        Assert.Equal((1.0, AmsuSettings.MaxWarnSeconds), (p.Amsu!.Opacity, p.Amsu.WarnSeconds));
        Assert.Equal((100, JudgmentSettings.MinOpacity), (p.Judgment!.FontSize, p.Judgment.Opacity));
        Assert.NotNull(p.Judgment.Characters);
        Assert.Equal(1.0, p.MeterBarOpacity);
    }

    [Fact]
    public void A_profile_is_copied_whole_but_the_target_keeps_its_screenshot()
    {
        var s = Loaded();
        var shot = new PixelRect(0, 0, 100, 100);
        s.Profiles[3].Shot = shot;
        (s.Overlay.Left, s.Amsu.On, s.Meter.BarOpacity) = (42, true, 0.4);

        Assert.True(s.CopyProfile(3));

        var p = s.Profiles[3];
        Assert.Equal((42.0, true, 0.4), (p.Overlay.Left, p.Amsu!.On, p.MeterBarOpacity));
        Assert.Equal(shot, p.Shot);
        s.Amsu.On = false;
        Assert.True(p.Amsu.On);   // a copy, not the same object
        Assert.Equal(0, s.ActiveProfile);
    }

    [Fact]
    public void A_file_with_shared_looks_gives_every_profile_the_shared_value_and_shares_none()
    {
        var s = Loaded(x =>
        {
            x.SharedLooks = [LookGroup.암수];
            x.Profiles[3].Amsu = x.Amsu.Copy();
            x.Profiles[3].Amsu!.Color = "#FFFFFF";   // stale: the shared value was the live one
        });

        Assert.Empty(s.SharedLooks!);
        Assert.All(s.Profiles, p => Assert.Equal("#112233", p.Amsu!.Color));
    }

    [Fact]
    public void An_older_file_without_marks_shares_none()
    {
        Assert.Empty(Loaded(x => x.SharedLooks = null).SharedLooks!);
    }

    [Fact]
    public void Applying_a_look_to_all_profiles_copies_the_live_value_once()
    {
        var s = Loaded();
        s.SwitchProfile(2);
        (s.Amsu.Color, s.Amsu.Left, s.Overlay.Opacity) = ("#ABCDEF", 500, 0.4);

        s.ApplyLookToAll(LookGroup.암수);

        Assert.All(s.Profiles, p => Assert.Equal("#ABCDEF", p.Amsu!.Color));
        s.SwitchProfile(0);
        Assert.Equal("#ABCDEF", s.Amsu.Color);
        Assert.NotEqual(500, s.Amsu.Left);        // the window's spot stays the profile's own
        Assert.NotEqual(0.4, s.Overlay.Opacity);  // 판 wasn't applied
        s.Amsu.Color = "#000000";
        s.SwitchProfile(2);
        Assert.Equal("#ABCDEF", s.Amsu.Color);    // not shared afterwards: each keeps its own again
    }

    [Fact]
    public void Every_look_switches_with_the_profile()
    {
        var s = Loaded();
        s.SwitchProfile(1);
        (s.Amsu.Color, s.Overlay.Opacity, s.Gauge.Fill) = ("#FFFFFF", 0.5, "#010203");

        s.SwitchProfile(0);

        Assert.Equal(("#112233", 0.9), (s.Amsu.Color, s.Overlay.Opacity));
        Assert.NotEqual("#010203", s.Gauge.Fill);
        Assert.False(new Settings().SharedLooks is { Count: > 0 });   // a new file shares nothing either
    }

    [Fact]
    public void Meter_debuff_and_count_windows_switch_spot_size_and_look_with_the_profile()
    {
        var s = Loaded();
        (s.Meter.Left, s.Debuffs.Top, s.Raids.Left, s.Raids.Width) = (10, 20, 30, 1.5);
        (s.Debuffs.Box(1).Width, s.Debuffs.HideWhenEmpty, s.Raids.Background, s.Raids.Opacity) = (1.4, true, "#112233", 0.7);
        s.SwitchProfile(1);
        (s.Meter.Left, s.Debuffs.Top, s.Raids.Left, s.Raids.Width) = (100, 200, 300, 0.8);
        (s.Debuffs.Box(1).Width, s.Debuffs.HideWhenEmpty, s.Raids.Background, s.Raids.Opacity) = (0.6, false, "#FFFFFF", 0.3);

        s.SwitchProfile(0);

        Assert.Equal((10.0, 20.0, 30.0, 1.5), (s.Meter.Left, s.Debuffs.Top, s.Raids.Left, s.Raids.Width));
        Assert.Equal((1.4, true, "#112233", 0.7), (s.Debuffs.Box(1).Width, s.Debuffs.HideWhenEmpty, s.Raids.Background, s.Raids.Opacity));
        s.SwitchProfile(1);
        Assert.Equal((100.0, 0.6, "#FFFFFF"), (s.Meter.Left, s.Debuffs.Box(1).Width, s.Raids.Background));
    }

    [Fact]
    public void An_older_file_s_profiles_start_with_today_s_meter_debuff_and_count_windows()
    {
        var s = Loaded(x => (x.Meter.Left, x.Raids.Background, x.Debuffs.HideWhenEmpty) = (55, "#ABABAB", true));

        s.SwitchProfile(3);

        Assert.Equal((55.0, "#ABABAB", true), (s.Meter.Left, s.Raids.Background, s.Debuffs.HideWhenEmpty));
    }

    [Fact]
    public void The_meter_window_s_look_is_per_profile_when_not_shared()
    {
        var s = Loaded(x => (x.Meter.MyColor, x.Meter.Look) = ("#FF0000", MeterLook.세로막대));
        Assert.All(s.Profiles, p => Assert.Equal("#FF0000", p.Meter!.My));   // an older file: today's in every 배치

        s.SwitchProfile(1);
        (s.Meter.MyColor, s.Meter.Background, s.Meter.Graph) = ("#00FF00", "#000000", false);
        s.SwitchProfile(0);

        Assert.Equal(("#FF0000", "#CC101418", true, MeterLook.세로막대), (s.Meter.MyColor, s.Meter.Background, s.Meter.Graph, s.Meter.Look));
        s.SwitchProfile(1);
        Assert.Equal(("#00FF00", false), (s.Meter.MyColor, s.Meter.Graph));
    }

    [Fact]
    public void Buff_bar_looks_are_per_profile_when_not_shared_and_on_off_isn_t()
    {
        var s = Loaded(x => x.Buffs.Edit(BuffKind.공증).Color = "#FF0000");
        s.SwitchProfile(1);
        (s.Buffs.Edit(BuffKind.공증).Color, s.Buffs.Edit(BuffKind.공증).Visible, s.Buffs.Drain) = ("#00FF00", false, false);
        s.Buffs.Edit(BuffKind.아덴피증).ShowLabel = false;

        s.SwitchProfile(0);

        Assert.Equal(("#FF0000", false, true), (s.Buffs.Of(BuffKind.공증).Color, s.Buffs.Of(BuffKind.공증).Visible, s.Buffs.Drain));
        Assert.True(s.Buffs.Of(BuffKind.아덴피증).ShowLabel);
        s.SwitchProfile(1);
        Assert.Equal(("#00FF00", false), (s.Buffs.Of(BuffKind.공증).Color, s.Buffs.Of(BuffKind.아덴피증).ShowLabel));
    }

    [Fact]
    public void Skill_box_looks_and_the_plate_are_per_profile_when_not_shared()
    {
        var s = Loaded(x => (x.Overlay.GoldColor, x.Overlay.BoxOpacity) = ("#123456", 0.7));
        s.SwitchProfile(1);
        (s.Overlay.GoldColor, s.Overlay.GoldThickness, s.Overlay.ShowPlate, s.Overlay.ReadyCountColor) = ("#FFFFFF", 4, false, "#FF0000");

        s.SwitchProfile(0);

        Assert.Equal(("#123456", 0.7, 2.0, true, "#000000"),
            (s.Overlay.GoldColor, s.Overlay.BoxOpacity, s.Overlay.GoldThickness, s.Overlay.ShowPlate, s.Overlay.ReadyCountColor));
        s.SwitchProfile(1);
        Assert.Equal(("#FFFFFF", false), (s.Overlay.GoldColor, s.Overlay.ShowPlate));
    }

    [Fact]
    public void Gauge_colours_and_class_bars_are_per_profile()
    {
        var s = Loaded(x => (x.Gauge.Fill, x.Gauge.Opacity) = ("#123456", 0.8));
        s.SwitchProfile(1);
        (s.Gauge.Fill, s.Gauge.Empty, s.Gauge.Opacity, s.Gauge.ReadMode) = ("#FFFFFF", "#000000", 0.5, GaugeReadMode.Predicted);
        GaugeBar.Edit(s, "바드", "").GlowEnabled = false;

        s.SwitchProfile(0);

        Assert.Equal(("#123456", "#3A3A3A", 0.8), (s.Gauge.Fill, s.Gauge.Empty, s.Gauge.Opacity));
        Assert.Equal(GaugeReadMode.Predicted, s.Gauge.ReadMode);   // a read setting, not a look
        Assert.True(GaugeBar.Edit(s, "바드", "").GlowEnabled);      // 클래스 전용 is per profile too (class-profiles.plan.md B)
        s.SwitchProfile(1);
        Assert.Equal(("#FFFFFF", 0.5), (s.Gauge.Fill, s.Gauge.Opacity));
        Assert.False(GaugeBar.Edit(s, "바드", "").GlowEnabled);
    }

    [Theory]
    [InlineData(0)]    // the active one
    [InlineData(-1)]
    [InlineData(5)]
    public void Copying_onto_itself_or_a_missing_profile_does_nothing(int to)
    {
        var s = Loaded();
        var before = JsonSerializer.Serialize(s.Profiles, SettingsStore.Json);

        Assert.False(s.CopyProfile(to));

        Assert.Equal(before, JsonSerializer.Serialize(s.Profiles, SettingsStore.Json));
    }

    [Fact]
    public void A_profile_is_copied_from_another_than_the_active_one()
    {
        var s = Loaded();
        s.Profiles[2].Overlay.Left = 77;
        s.Profiles[2].Shot = null;
        var shot = new PixelRect(0, 0, 50, 50);
        s.Profiles[4].Shot = shot;

        Assert.True(s.CopyProfile(2, 4));

        Assert.Equal(77, s.Profiles[4].Overlay.Left);
        Assert.Equal(shot, s.Profiles[4].Shot);
        Assert.Equal(0, s.ActiveProfile);
    }

    [Fact]
    public void Copying_into_the_active_profile_puts_it_on_screen_at_once()
    {
        var s = Loaded();
        s.Profiles[2].Overlay.Left = 77;

        Assert.True(s.CopyProfile(2, 0));

        Assert.Equal(77, s.Overlay.Left);
        Assert.Equal(0, s.ActiveProfile);
    }

    [Fact]
    public void Copying_a_profile_onto_itself_from_the_pair_form_does_nothing()
    {
        Assert.False(Loaded().CopyProfile(2, 2));
    }
}
