using GaugeOverlay.Core.Cooldown;

namespace GaugeOverlay.Tests;

/// <summary>The cadence both debuff rows share (RowCadence): the walk's miss rule here; the timings in DebuffScheduleTests.</summary>
public class RowCadenceTests
{
    [Theory]
    [InlineData("xxx..", 3)]     // filled from the left, the walk ends at two empty slots
    [InlineData("xx.xx..", 5)]   // one hidden frame (a damage number) is passed: the debuffs after it still count
    [InlineData("xx..xx", 2)]    // two empty in a row end it: nothing past them is looked at
    [InlineData(".x", 2)]        // the first one hidden
    [InlineData("..x", 0)]
    [InlineData("", 0)]
    public void The_walk_passes_one_empty_slot(string row, int filled) =>
        Assert.Equal(filled, RowCadence.Filled(row.Length, i => row[i] == 'x'));

    [Fact]
    public void A_shift_an_old_identification_or_an_unread_timer_identifies_again()
    {
        var at = TimeSpan.FromSeconds(10);
        Assert.False(RowCadence.Identify(3, 3, at, false, at + TimeSpan.FromMilliseconds(200)));
        Assert.True(RowCadence.Identify(3, 2, at, false, at + TimeSpan.FromMilliseconds(200)));
        Assert.True(RowCadence.Identify(3, 3, at, false, at + TimeSpan.FromMilliseconds(500)));
        Assert.True(RowCadence.Identify(3, 3, at, true, at + TimeSpan.FromMilliseconds(200)));
    }
}
