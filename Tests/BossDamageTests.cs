using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Boss damage taken since the 전분 record started (BossDamage, spec § C): each segment of the HP bar adds its own
/// drop: a phase (a new bar, a new max) or a rise within one (a shield, a heal) starts a new segment.</summary>
public class BossDamageTests
{
    static long Taken(params (long Cur, long Max)?[] reads) =>
        BossDamage.Taken(reads.Aggregate(BossDamageState.None, BossDamage.Update));

    [Fact]
    public void The_shield_kept_is_the_one_of_the_read_the_filter_kept()
    {
        var s = BossDamage.Update(BossDamageState.None, (100, 200), 50);
        Assert.Equal(50, s.LastShield);
        s = BossDamage.Update(s, (100, 200), 40);
        Assert.Equal((150L, 50L), (s.Last!.Value, s.LastShield));   // two reads: the higher
        s = BossDamage.Update(s, (100, 200), 30);
        Assert.Equal((140L, 40L), (s.Last!.Value, s.LastShield));   // the median of 150, 140, 130
        s = BossDamage.Update(s, (100, 200), 0);
        Assert.Equal((130L, 30L), (s.Last!.Value, s.LastShield));   // of 140, 130, 100
    }

    [Fact]
    public void One_phase_counts_the_drop_since_the_first_read()
    {
        Assert.Equal(0, Taken());
        Assert.Equal(40, Taken((100, 100), (90, 100), (80, 100), (70, 100), (60, 100), (60, 100)));
    }

    [Fact]
    public void A_record_started_mid_phase_counts_from_its_first_read() =>
        Assert.Equal(20, Taken((70, 100), (60, 100), (50, 100), (50, 100)));

    [Fact]
    public void A_phase_change_adds_each_bar_drop()
    {
        // 100 → 0, then a new bar of 50 → 30: 100 + 20.
        Assert.Equal(120, Taken((100, 100), (50, 100), (0, 100), (50, 50), (40, 50), (30, 50), (30, 50)));
    }

    [Fact]
    public void A_rise_within_a_phase_starts_a_new_segment()
    {
        // a shield: 100 → 60 (held for two reads: one read alone is taken for a misread), up to 80 and staying, then
        // 80 → 70: 40 + 10.
        Assert.Equal(50, Taken((100, 100), (80, 100), (60, 100), (60, 100), (80, 100), (80, 100), (75, 100), (70, 100), (70, 100)));
    }

    [Fact]
    public void A_rise_right_after_a_steady_fall_keeps_the_whole_drop() =>
        // 100 → 70 read by read, then a shield to 150: the 30 counts in full (the filter lags a read behind the fall).
        Assert.Equal(30, Taken((100, 200), (90, 200), (80, 200), (70, 200), (150, 200), (150, 200)));

    [Fact]
    public void A_one_read_misread_neither_counts_nor_splits()
    {
        Assert.Equal(43, Taken((100, 100), (60, 100), (5, 100), (58, 100), (57, 100), (57, 100)));      // a dip
        Assert.Equal(43, Taken((100, 100), (60, 100), (99, 100), (58, 100), (57, 100), (57, 100)));     // a spike
    }

    [Fact]
    public void Unreadable_or_impossible_reads_change_nothing()
    {
        Assert.Equal(40, Taken((100, 100), (80, 100), null, (60, 100), (60, 100)));
        Assert.Equal(40, Taken((100, 100), (80, 100), (150, 100), (60, 100), (60, 100)));
    }

    [Fact]
    public void A_shield_in_brackets_counts_as_hp_and_breaking_it_as_damage()
    {
        // 100 → 60, a shield of 40 (read as "60/100(+40)": up to 100), broken to 0 over three reads: 40 + 40, even past the max.
        (long, long, long)[] reads = [(100, 100, 0), (60, 100, 0), (60, 100, 0), (60, 100, 40), (60, 100, 40), (60, 100, 20), (60, 100, 0), (60, 100, 0)];
        Assert.Equal(80, BossDamage.Taken(reads.Aggregate(BossDamageState.None, (st, r) => BossDamage.Update(st, (r.Item1, r.Item2), r.Item3))));
        Assert.Equal(1150, BossDamage.Update(BossDamageState.None, (100, 100), 1050).Last);   // a shield over a full bar: not a misread
    }
}
