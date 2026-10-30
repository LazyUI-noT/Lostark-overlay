using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 → DPS 미터기 (MeterSettings): off and not sending until turned on, kept through the file.</summary>
public class MeterSettingsTests
{
    [Fact]
    public void A_panel_spot_is_read_only_inside_the_game_area()
    {
        var vp = new Viewport(100, 50, 1920, 1080);
        Assert.True(new PixelRect(1700, 800, 300, 300).Inside(vp));
        Assert.False(new PixelRect(1800, 800, 300, 300).Inside(vp));
        Assert.False(new PixelRect(0, 0, 100000, 20).Inside(vp));
    }

    static Settings RoundTrip(Settings s) => SettingsStore.Normalize(
        JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(s, SettingsStore.Json), SettingsStore.Json)!);

    [Fact]
    public void Off_and_not_sending_by_default()
    {
        var m = new Settings().Meter;
        Assert.False(m.On || m.Send);
        Assert.Equal((MeterScope.파티, MeterValue.지분, MeterLook.선그래프), (m.Scope, m.Value, m.Look));
        Assert.True(m.Graph && m.Show && m.Record && m.BuffBars);   // the window, its graph, 전투 기록 and the buff bars, once the meter is on
    }

    [Fact]
    public void Settings_survive_a_save_and_a_load()
    {
        var s = new Settings();
        (s.Meter.On, s.Meter.Send, s.Meter.Scope, s.Meter.Look) = (true, true, MeterScope.파티별, MeterLook.파티비교);
        s.Meter.Panels["1920×1080"] = new PixelRect(1500, 700, 380, 220);
        var back = RoundTrip(s).Meter;
        Assert.Equal((true, true, MeterScope.파티별, MeterLook.파티비교), (back.On, back.Send, back.Scope, back.Look));
        Assert.Equal(new PixelRect(1500, 700, 380, 220), back.Panels["1920×1080"]);
    }

    [Fact]
    public void Hand_edited_numbers_are_repaired()
    {
        var s = new Settings();
        s.Meter.Opacity = 9;
        s.Meter.Panels["x"] = new PixelRect(0, 0, -5, 10);
        var back = RoundTrip(s).Meter;
        Assert.Equal(1.0, back.Opacity);
        Assert.Empty(back.Panels);
    }

    [Fact]
    public void Party_comparison_needs_both_parties_in_view()
    {
        var m = new MeterSettings { Look = MeterLook.파티비교, Scope = MeterScope.파티 };
        Assert.Equal(MeterLook.행, m.ShownLook);
        m.Scope = MeterScope.공대;
        Assert.Equal(MeterLook.파티비교, m.ShownLook);
    }
}
