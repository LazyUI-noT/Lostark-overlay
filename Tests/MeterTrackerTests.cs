using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The meter's once-a-second step (MeterTracker): the 전분 timer drives the record, the boss HP its damage taken, and
/// my figures for the party come from both.</summary>
public class MeterTrackerTests
{
    static JeonbunReading Panel(int timer, long damage = 20_000_000_000) => new(damage, damage / Math.Max(1, timer), timer, false);

    [Fact]
    public void A_new_record_starts_the_boss_damage_over()
    {
        var s = MeterStatus.None;
        s = MeterTracker.Step(s, (100, 100), Panel(10), true, S(0));
        s = MeterTracker.Step(s, (80, 100), Panel(11), true, S(1));
        s = MeterTracker.Step(s, (60, 100), Panel(12), true, S(2));
        Assert.True(BossDamage.Taken(s.Boss) > 0);
        s = MeterTracker.Step(s, (55, 100), Panel(0), true, S(3));   // 전분 reset: confirmed by the next read
        Assert.Equal(1, s.Clock.Fight);
        s = MeterTracker.Step(s, (55, 100), Panel(1), true, S(4));
        Assert.Equal(0, BossDamage.Taken(s.Boss));
        Assert.Equal(2, s.Clock.Fight);
    }

    [Fact]
    public void No_panel_means_no_figures_to_send()
    {
        var s = MeterTracker.Step(MeterStatus.None, (100, 100), null, false, S(0));
        Assert.False(s.PanelFound);
        Assert.Null(MeterTracker.Mine(s, MeterRole.딜러));
    }

    [Fact]
    public void My_figures_come_from_the_panel_and_the_boss()
    {
        var s = MeterStatus.None;
        foreach (var (hp, t) in new[] { (100L, 0), (90L, 1), (80L, 2), (80L, 3) }) s = MeterTracker.Step(s, (hp, 100), Panel(5 + t), true, S(t));
        var mine = MeterTracker.Mine(s, MeterRole.서포터)!;
        Assert.Equal((20_000_000_000L, 8, MeterRole.서포터, 1), (mine.Damage, mine.Timer!.Value, mine.Role, mine.Fight));
        Assert.Equal(BossDamage.Taken(s.Boss), mine.BossTaken);
    }
    [Fact]
    public void A_few_missed_reads_keep_the_last_figures()
    {
        var s = MeterTracker.Step(MeterStatus.None, (100, 100), Panel(10), true, S(0));
        for (int t = 1; t <= 4; t++)
        {
            s = MeterTracker.Step(s, (100, 100), null, false, S(t));
            Assert.True(s.PanelFound);
            Assert.Equal(10, s.Panel!.Timer);
        }
    }

    [Fact]
    public void A_long_gap_clears_the_figures()
    {
        var s = MeterTracker.Step(MeterStatus.None, (100, 100), Panel(10), true, S(0));
        for (int t = 1; t <= 6; t++) s = MeterTracker.Step(s, (100, 100), null, false, S(t));
        Assert.False(s.PanelFound);
        Assert.Null(s.Panel);
    }
}
