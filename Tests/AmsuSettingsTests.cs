using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 for the 암수 창 (amsu.plan.md Task 5): defaults, limits, an old settings file, the 표시 switches.</summary>
public sealed class AmsuSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "settings.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void It_starts_off_as_a_bar_with_a_warning_under_5_seconds()
    {
        var a = new Settings().Amsu;
        Assert.False(a.On);
        Assert.Equal((AmsuLook.막대, BarDirection.오른쪽, 5, 1.0), (a.Look, a.Direction, a.WarnSeconds, a.Opacity));
        Assert.True(double.IsNaN(a.Left));
    }

    [Fact]
    public void It_reads_high_by_default_and_a_bad_rate_falls_back_to_high()
    {
        Assert.Equal(AmsuRate.높음, new Settings().Amsu.Rate);
        var a = new AmsuSettings { Rate = (AmsuRate)9 };
        a.Clamp();
        Assert.Equal(AmsuRate.높음, a.Rate);
    }

    [Fact]
    public void Out_of_range_values_are_brought_back()
    {
        var a = new AmsuSettings { Opacity = 3, WarnSeconds = 99, Width = 50, Height = double.NaN, Look = (AmsuLook)9, Direction = (BarDirection)9 };
        a.Clamp();
        Assert.Equal((1.0, AmsuSettings.MaxWarnSeconds, ClassProfiles.MaxScale, 1.0), (a.Opacity, a.WarnSeconds, a.Width, a.Height));
        Assert.Equal((AmsuLook.막대, BarDirection.오른쪽), (a.Look, a.Direction));
    }

    [Fact]
    public void A_settings_file_from_before_loads_the_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"Amsu":null,"RecordKeep":20}""");
        var s = new SettingsStore(FilePath).Load(out _);
        Assert.NotNull(s.Amsu);
        Assert.False(s.Amsu.On);
    }

    [Fact]
    public void The_show_switches_turn_it_on_and_off()
    {
        var s = new Settings();
        Assert.False(TrayToggles.IsShown(s, OverlayPart.Amsu));
        TrayToggles.Show(s, OverlayPart.Amsu, true);
        Assert.True(s.Amsu.On && TrayToggles.IsShown(s, OverlayPart.Amsu));
        TrayToggles.Show(s, OverlayPart.Amsu, false);
        Assert.False(s.Amsu.On);
        Assert.False(s.Raids.DebuffShown());   // not the 디버프 창's switch
    }
}
