using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Which key presses drop the gauge at once (ImmediateSpend): 바드's first use spends, the free recast within 8 s
/// (after the skill's 1 s cooldown) takes nothing and the use after it spends again; 도화가's Z and C never share a state.</summary>
public class ImmediateSpendTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    static GaugeRule Bard() => new([0.25, 0.5, 0.75], null, null,
        [new GaugeSpend([SlotId.아덴1, SlotId.아덴2], 3, Immediate: true, FreeRecast: 8)]);

    static GaugeRule Dohwaga() => new([0.3333, 0.6667], null, null,
        [new GaugeSpend([SlotId.아덴1], 2, Immediate: true), new GaugeSpend([SlotId.아덴2], 1, Immediate: true)]);

    [Fact]
    public void The_first_use_spends_the_free_recast_does_not_and_the_use_after_it_spends_again()
    {
        var s = new ImmediateSpend();
        Assert.Equal(0.25, s.Press(Bard(), SlotId.아덴1, 1.0, S(0)));
        Assert.Null(s.Press(Bard(), SlotId.아덴2, 1.0, S(1.5)));       // Z or X: the free one
        Assert.Equal(0.25, s.Press(Bard(), SlotId.아덴1, 1.0, S(3)));  // the third spends
    }

    [Fact]
    public void A_recast_after_the_free_window_is_a_new_first_use()
    {
        var s = new ImmediateSpend();
        s.Press(Bard(), SlotId.아덴1, 1.0, S(0));
        Assert.Equal(0.25, s.Press(Bard(), SlotId.아덴1, 1.0, S(9.2)));
    }

    /// <summary>The 8 s start after the first use's 1 s cooldown: a recast at 8.8 s is still free.</summary>
    [Fact]
    public void The_free_window_starts_after_the_cooldown()
    {
        var s = new ImmediateSpend();
        s.Press(Bard(), SlotId.아덴1, 1.0, S(0));
        Assert.Null(s.Press(Bard(), SlotId.아덴1, 1.0, S(8.8)));
    }

    /// <summary>The cast the engine sees after a press, and a press during the skill's 1 s cooldown, are not the recast.</summary>
    [Fact]
    public void The_cast_after_a_press_and_a_press_in_the_cooldown_do_not_use_up_the_free_recast()
    {
        var s = new ImmediateSpend();
        s.Press(Bard(), SlotId.아덴1, 1.0, S(0));
        Assert.Null(s.Press(Bard(), SlotId.아덴1, 1.0, S(0.3)));
        Assert.Null(s.Press(Bard(), SlotId.아덴2, 1.0, S(0.6)));
        Assert.Null(s.Press(Bard(), SlotId.아덴2, 1.0, S(2)));         // the free recast is still there
        Assert.NotNull(s.Press(Bard(), SlotId.아덴1, 1.0, S(4)));
    }

    [Fact]
    public void A_free_recast_with_no_full_section_left_still_counts_as_the_recast()
    {
        var s = new ImmediateSpend();
        s.Press(Bard(), SlotId.아덴1, 0.8, S(0));
        Assert.Null(s.Press(Bard(), SlotId.아덴1, 0.05, S(2)));
        Assert.Equal(0.25, s.Press(Bard(), SlotId.아덴1, 1.0, S(4)));
    }

    [Fact]
    public void A_skill_without_a_free_recast_keeps_its_slots_apart()
    {
        var s = new ImmediateSpend();
        Assert.Equal(0.3333, s.Press(Dohwaga(), SlotId.아덴1, 1.0, S(0))!.Value, 3);
        Assert.NotNull(s.Press(Dohwaga(), SlotId.아덴2, 1.0, S(0.2)));  // C is not held back by Z
        Assert.Null(s.Press(Dohwaga(), SlotId.아덴1, 1.0, S(0.3)));     // the cast seen after Z
    }

    [Fact]
    public void A_slot_that_does_not_spend_at_once_is_left_to_the_reads()
    {
        var rule = new GaugeRule([0.5], null, null, [new GaugeSpend([SlotId.아덴1], 1)]);
        Assert.Null(new ImmediateSpend().Press(rule, SlotId.아덴1, 1.0, S(0)));
    }
}
