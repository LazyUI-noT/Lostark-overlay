using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class RowDefaultTests
{
    [Fact]
    public void Differs_FreshSettings_IsFalse()
    {
        Assert.False(RowDefault.Differs(new Settings(), s => s.Overlay.Opacity));
        Assert.False(RowDefault.Differs(new Settings(), s => s.RecordAutoSave));
        Assert.False(RowDefault.Differs(new Settings(), s => s.Theme));
    }

    [Fact]
    public void Differs_ChangedValue_IsTrue()
    {
        var s = new Settings { RecordKeep = 10, RecordAutoSave = false };

        Assert.True(RowDefault.Differs(s, x => x.RecordKeep));
        Assert.True(RowDefault.Differs(s, x => x.RecordAutoSave));
    }

    [Fact]
    public void Differs_DoubleWithinSliderRounding_IsFalse()
    {
        var s = new Settings();
        s.Overlay.Opacity = 0.9 + 1e-12;

        Assert.False(RowDefault.Differs(s, x => x.Overlay.Opacity));
    }

    [Fact]
    public void Differs_NaNDefaultStillNaN_IsFalse()
    {
        Assert.False(RowDefault.Differs(new Settings(), s => s.Overlay.Left));
    }

    [Fact]
    public void Differs_NaNDefaultSetToNumber_IsTrue()
    {
        var s = new Settings();
        s.Overlay.Left = 100;

        Assert.True(RowDefault.Differs(s, x => x.Overlay.Left));
    }

    [Fact]
    public void Reset_CopiesTheDefaultInAndLeavesOtherValues()
    {
        var s = new Settings { RecordKeep = 10, RecordPauseSeconds = 30 };

        RowDefault.Reset(s, x => x.RecordKeep, (x, v) => x.RecordKeep = v);

        Assert.Equal(new Settings().RecordKeep, s.RecordKeep);
        Assert.Equal(30, s.RecordPauseSeconds);
    }

    [Fact]
    public void Reset_DoesNotChangeTheSharedDefaults()
    {
        var s = new Settings();
        RowDefault.Reset(s, x => x.Overlay.Opacity, (x, v) => x.Overlay.Opacity = v);
        s.Overlay.Opacity = 0.2;

        Assert.True(RowDefault.Differs(s, x => x.Overlay.Opacity));
    }
}
