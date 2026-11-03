using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 전분 record's lifecycle from its timer read once a second (FightClock, spec § C Lifecycle, § D Fight clock).</summary>
public class FightClockTests
{
    static FightClockState Run(params (int? Timer, double At)[] reads) =>
        reads.Aggregate(FightClockState.None, (s, r) => FightClock.Update(s, r.Timer, S(r.At)));

    [Fact]
    public void A_counting_timer_runs()
    {
        var s = Run((3, 0), (4, 1), (5, 2));
        Assert.False(s.Frozen);
        Assert.Equal((5, 1), (s.Timer!.Value, s.Fight));
    }

    [Fact]
    public void The_same_value_for_a_second_and_a_half_freezes_until_it_moves()
    {
        Assert.False(Run((5, 0), (5, 1.4)).Frozen);
        Assert.True(Run((5, 0), (5, 1.0), (5, 1.6)).Frozen);
        Assert.False(Run((5, 0), (5, 1.0), (5, 1.6), (6, 2.6)).Frozen);
    }

    [Fact]
    public void The_timer_going_back_is_a_new_fight()
    {
        var s = Run((12, 0), (13, 1), (0, 2), (1, 3));
        Assert.Equal(2, s.Fight);
        Assert.Equal(1, s.Timer);
    }

    [Fact]
    public void One_lower_misread_is_not_a_new_fight()
    {
        var s = Run((124, 0), (125, 1), (25, 2), (126, 3));
        Assert.Equal((1, 126), (s.Fight, s.Timer!.Value));
        Assert.Equal(1, Run((124, 0), (125, 1), (25, 2)).Fight);
    }

    [Fact]
    public void Unreadable_reads_change_nothing()
    {
        var s = Run((5, 0), (6, 1));
        Assert.Equal(s, FightClock.Update(s, null, S(9)));
    }

    [Fact]
    public void The_first_read_starts_fight_one() => Assert.Equal(1, Run((40, 0)).Fight);

    /// <summary>The next gate has no loading screen, but its 전분 record starts at 00:00 (user, 2026-11-03): a record after
    /// another one is a restart; the first one seen, and a lower read not yet confirmed, are not.</summary>
    [Fact]
    public void A_record_after_another_one_is_a_restart()
    {
        var before = Run((12, 0), (13, 1));
        Assert.False(FightClock.Restarted(FightClockState.None, Run((40, 0))));
        Assert.False(FightClock.Restarted(before, Run((12, 0), (13, 1), (0, 2))));
        Assert.True(FightClock.Restarted(Run((12, 0), (13, 1), (0, 2)), Run((12, 0), (13, 1), (0, 2), (1, 3))));
        Assert.False(FightClock.Restarted(before, before));
    }
}
