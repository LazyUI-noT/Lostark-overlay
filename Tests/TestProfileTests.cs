using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>[Test]: 테스트 모드 has its own settings; on returns to them, off returns to the ones before.</summary>
public class TestProfileTests
{
    [Fact]
    public void A_copy_shares_nothing_with_the_original()
    {
        var s = new Settings();
        s.Meter.On = true;
        var copy = SettingsStore.Copy(s);
        copy.Meter.On = false;
        Assert.True(s.Meter.On);
    }

    [Fact]
    public void Swapping_in_takes_the_options_and_the_places()
    {
        var live = new Settings();
        var test = new Settings();
        test.Meter.On = !live.Meter.On;
        test.Overlay.Left = 123;
        test.ActiveProfile = 2;
        live.SwapIn(test);
        Assert.Equal(test.Meter.On, live.Meter.On);
        Assert.Equal(123, live.Overlay.Left);
        Assert.Equal(2, live.ActiveProfile);
    }

    [Fact]
    public void Swapping_in_keeps_what_belongs_to_the_install()
    {
        var live = new Settings();
        live.Screens.ReadBehind = true;
        live.Screens.Display = "A";
        live.Hotkeys.TestMode = "Ctrl+T";
        var test = new Settings();
        live.SwapIn(test);
        Assert.True(live.Screens.ReadBehind);
        Assert.Equal("A", live.Screens.Display);
        Assert.Equal("Ctrl+T", live.Hotkeys.TestMode);
    }

    [Fact]
    public void Swapping_in_keeps_what_the_app_learned()
    {
        var live = new Settings { ActiveCharacter = "kept" };
        live.SwapIn(new Settings { ActiveCharacter = "old" });
        Assert.Equal("kept", live.ActiveCharacter);
    }

    [Fact]
    public void Round_trip_brings_the_settings_before_back()
    {
        var live = new Settings();
        live.Meter.On = true;
        live.Overlay.Left = 50;
        var before = SettingsStore.Copy(live);
        live.SwapIn(SettingsStore.Copy(live));
        live.Meter.On = false;
        live.Overlay.Left = 999;
        live.SwapIn(before);
        Assert.True(live.Meter.On);
        Assert.Equal(50, live.Overlay.Left);
    }

    [Fact]
    public void A_cleared_history_has_no_steps()
    {
        var h = new SettingsHistory();
        h.Record("a");
        h.Clear();
        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);
    }
}
