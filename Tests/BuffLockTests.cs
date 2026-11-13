using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>What the buff lock leaves on screen (buff-lock.design.md): 공증 only, capped at 85%.</summary>
public class BuffLockTests
{
    [Theory]
    [InlineData(0.62, false, "62%")]
    [InlineData(0.97, false, "97%")]
    [InlineData(0.62, true, "62%")]
    [InlineData(0.843, true, "84%")]
    [InlineData(0.846, true, "85+%")]   // would read 85%: capped
    [InlineData(0.97, true, "85+%")]
    [InlineData(1.4, false, "100%")]
    [InlineData(-0.2, true, "0%")]
    public void Figure_caps_at_85_when_locked(double share, bool locked, string expected) =>
        Assert.Equal(expected, BuffLock.Figure(share, locked));

    [Theory]
    [InlineData(0.97, false, 0.97)]
    [InlineData(0.97, true, 0.85)]
    [InlineData(0.5, true, 0.5)]
    [InlineData(1.5, false, 1.0)]
    public void Bar_stops_at_85_when_locked(double share, bool locked, double expected) =>
        Assert.Equal(expected, BuffLock.Bar(share, locked), 6);

    static MeterView View() => new([], null, 0, null, false, true, new MeterHistory(),
        BuffCover: [0.9, 0.5, 0.4], BuffParty: [0.88, 0.6, 0.3],
        BuffSplit: [[(1UL, "가", 0.6), (2UL, "나", 0.4)], [(1UL, "가", 1.0)], [(2UL, "나", 1.0)]]);

    [Fact]
    public void Locked_meter_keeps_only_공증()
    {
        var v = BuffLock.Apply(View(), locked: true);
        Assert.True(v.BuffLocked);
        Assert.Equal([0.9], v.BuffCover!);
        Assert.Equal([0.88], v.BuffParty!);
        Assert.Single(v.BuffSplit!);
        Assert.Equal(2, v.BuffSplit![0].Count);
    }

    [Fact]
    public void Unlocked_meter_is_untouched()
    {
        var before = View();
        Assert.Same(before, BuffLock.Apply(before, locked: false));
    }

    [Fact]
    public void Locked_meter_with_nothing_read_stays_empty()
    {
        var v = BuffLock.Apply(new MeterView([], null, 0, null, false, true, new MeterHistory()), locked: true);
        Assert.Null(v.BuffCover);
        Assert.Null(v.BuffParty);
        Assert.Null(v.BuffSplit);
    }

    [Fact]
    public void Locked_timeline_has_no_초각스_lane()
    {
        Assert.Equal([BuffKind.공증, BuffKind.아덴피증, BuffKind.초각스피증], BuffLock.TimelineKinds(false));
        Assert.Equal([BuffKind.공증, BuffKind.아덴피증], BuffLock.TimelineKinds(true));
    }

    static FightRecord Fight(DateTime started, double seconds) => new(started, null, seconds, [], "나", 0, null, [], [], [], []);

    [Fact]
    public void Locked_아덴_피증_shows_10_minutes_after_the_fight_ends()
    {
        var start = new DateTime(2026, 11, 12, 21, 0, 0);
        var r = Fight(start, 300);   // ends 21:05
        Assert.False(BuffLock.LineShown(BuffKind.아덴피증, true, r, start.AddMinutes(14.9)));
        Assert.True(BuffLock.LineShown(BuffKind.아덴피증, true, r, start.AddMinutes(15)));
        Assert.True(BuffLock.LineShown(BuffKind.아덴피증, false, r, start.AddMinutes(6)));
        Assert.True(BuffLock.LineShown(BuffKind.공증, true, r, start.AddMinutes(6)));
        Assert.False(BuffLock.LineShown(BuffKind.초각스피증, true, r, start.AddDays(3)));
    }

    [Fact]
    public void Locked_shows_a_percent_for_공증_only()
    {
        Assert.True(BuffLock.PercentShown(BuffKind.공증, true));
        Assert.False(BuffLock.PercentShown(BuffKind.아덴피증, true));
        Assert.False(BuffLock.PercentShown(BuffKind.초각스피증, true));
        Assert.All(Enum.GetValues<BuffKind>(), k => Assert.True(BuffLock.PercentShown(k, false)));
    }
}
