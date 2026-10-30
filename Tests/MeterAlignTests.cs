using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Lining the party's samples up on one 전분 second (MeterAlign): a sample that arrives late (network, relay) is not
/// compared with a newer one from someone else.</summary>
public class MeterAlignTests
{
    static MeterSample P(ulong id, int timer, long damage, long boss = 1000) =>
        new(id, $"p{id}", 0, MeterRole.딜러, 1, damage, 0, timer, true, 1, boss);

    [Fact]
    public void Everyone_is_taken_at_the_newest_second_all_have_reached()
    {
        var a = new MeterAlign();
        a.Align([P(1, 10, 100), P(2, 10, 200)], null);
        var (shared, _) = a.Align([P(1, 11, 150, boss: 1100), P(2, 10, 200)], null);   // 2's second 11 is still on its way
        Assert.Equal([(1UL, 10, 100L, 1000L), (2UL, 10, 200L, 1000L)], shared.Select(s => (s.Id, s.Timer!.Value, s.Damage, s.BossTaken)));
    }

    [Fact]
    public void My_own_figures_are_taken_at_the_same_second()
    {
        var a = new MeterAlign();
        a.Align([P(2, 20, 500)], P(1, 20, 300));
        var (_, mine) = a.Align([P(2, 20, 500)], P(1, 21, 330));
        Assert.Equal((20, 300L), (mine!.Timer!.Value, mine.Damage));
    }

    [Fact]
    public void A_member_far_behind_does_not_hold_the_others_back()
    {
        var a = new MeterAlign();
        a.Align([P(1, 29, 90), P(2, 5, 10)], null);
        var (shared, _) = a.Align([P(1, 30, 100), P(2, 5, 10)], null);
        Assert.Equal(30, shared.Single(s => s.Id == 1).Timer);
    }

    [Fact]
    public void A_new_record_forgets_the_old_seconds()
    {
        var a = new MeterAlign();
        a.Align([P(1, 40, 900), P(2, 40, 900)], null);
        var (shared, _) = a.Align([P(1, 1, 10), P(2, 2, 20)], null);
        Assert.Equal([10L, 20L], shared.Select(s => s.Damage));   // nothing from the old record is used
    }

    [Fact]
    public void Samples_without_a_timer_pass_as_they_are()
    {
        var (shared, mine) = new MeterAlign().Align([P(1, 10, 100) with { Timer = null }], null);
        Assert.Equal(100, Assert.Single(shared).Damage);
        Assert.Null(mine);
    }
}
