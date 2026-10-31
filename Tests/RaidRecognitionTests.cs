using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Which gate is being fought (RaidRecognition: two agreeing name reads, kept through misses) and what to read for it
/// (RaidReadPlan: 자동 or a gate picked by hand, and which windows are on).</summary>
public class RaidRecognitionTests
{
    static readonly RaidGate Mordum = new("act3-g3", "3막", "3관문", ["심연의 징벌자, 모르둠"], [], Raid.모르둠);
    static readonly RaidGate Other = new("act3-g2", "3막", "2관문", ["x"], [], Raid.없음);
    static readonly IReadOnlyList<RaidGate> Gates = [Mordum, Other];

    static RaidRecognitionState Run(params (string? Read, double At)[] reads) =>
        reads.Aggregate(RaidRecognitionState.None, (s, r) => RaidRecognition.Update(s, r.Read, S(r.At)));

    [Fact]
    public void A_gate_is_recognised_after_two_agreeing_reads()
    {
        Assert.Null(Run(("act3-g3", 0)).Gate);
        Assert.Equal("act3-g3", Run(("act3-g3", 0), ("act3-g3", 0.5)).Gate);
        Assert.Equal("act3-g3", Run(("act3-g3", 0), ("act3-g3", 0.5), ("act3-g2", 1)).Gate);   // one other read: kept
        Assert.Equal("act3-g2", Run(("act3-g3", 0), ("act3-g3", 0.5), ("act3-g2", 1), ("act3-g2", 1.5)).Gate);
    }

    [Fact]
    public void Misses_keep_the_gate_for_ten_seconds()
    {
        Assert.Equal("act3-g3", Run(("act3-g3", 0), ("act3-g3", 0.5), (null, 10.4)).Gate);
        Assert.Null(Run(("act3-g3", 0), ("act3-g3", 0.5), (null, 10.6)).Gate);
    }

    static RaidSettings Count(bool auto = true, bool countAuto = true)
    {
        var s = new RaidSettings { Auto = auto, ManualGate = auto ? null : "act3-g3" };
        (s.Gate("act3-g3").CountWindow, s.Gate("act3-g3").CountAuto) = (true, countAuto);
        return s;
    }

    [Fact]
    public void Windows_off_read_nothing() =>
        Assert.Equal((false, false, (RaidGate?)null), RaidReadPlan.Of(new RaidSettings(), Gates, "act3-g3"));

    [Fact]
    public void Unrecognised_with_자동_reads_the_name_and_shows_no_count() =>
        Assert.Equal((true, false, (RaidGate?)null), RaidReadPlan.Of(Count(), Gates, null));

    [Fact]
    public void Recognised_reads_the_count_for_that_gate() =>
        Assert.Equal((true, true, (RaidGate?)Mordum), RaidReadPlan.Of(Count(), Gates, "act3-g3"));

    [Fact]
    public void Another_gate_recognised_shows_no_count() =>
        Assert.Equal((true, false, (RaidGate?)Other), RaidReadPlan.Of(Count(), Gates, "act3-g2"));

    [Fact]
    public void A_gate_picked_by_hand_wins_over_자동() =>
        Assert.Equal((false, true, (RaidGate?)Mordum), RaidReadPlan.Of(Count(auto: false), Gates, "act3-g2"));

    [Fact]
    public void A_count_window_not_on_자동_shows_without_recognition() =>
        Assert.Equal((false, true, (RaidGate?)Mordum), RaidReadPlan.Of(Count(countAuto: false), Gates, null));   // nothing waits on the name

    [Fact]
    public void A_debuff_window_on_자동_needs_the_name_read()
    {
        var s = new RaidSettings();
        s.Gate("act3-g3").DebuffWindow = true;
        Assert.Equal((true, false, (RaidGate?)null), RaidReadPlan.Of(s, Gates, null));
        s.Gate("act3-g3").DebuffAuto = false;
        Assert.Equal((false, false, (RaidGate?)null), RaidReadPlan.Of(s, Gates, null));
    }

    [Fact]
    public void The_debuff_window_follows_its_gate()
    {
        var s = new RaidSettings();
        Assert.Equal(Raid.없음, RaidReadPlan.DebuffRaid(s, Gates, "act3-g3"));   // off
        s.Gate("act3-g3").DebuffWindow = true;
        Assert.Equal(Raid.없음, RaidReadPlan.DebuffRaid(s, Gates, null));        // 자동, not recognised
        Assert.Equal(Raid.모르둠, RaidReadPlan.DebuffRaid(s, Gates, "act3-g3"));
        Assert.Equal(Raid.없음, RaidReadPlan.DebuffRaid(s, Gates, "act3-g2"));
        (s.Auto, s.ManualGate) = (false, "act3-g3");
        Assert.Equal(Raid.모르둠, RaidReadPlan.DebuffRaid(s, Gates, null));      // picked by hand
        (s.Auto, s.Gate("act3-g3").DebuffAuto) = (true, false);
        Assert.Equal(Raid.모르둠, RaidReadPlan.DebuffRaid(s, Gates, null));      // always, as before the 레이드 table
    }

    [Fact]
    public void The_main_줄_수_switch_off_reads_no_count_and_keeps_each_gate()
    {
        var s = Count(countAuto: false);
        s.CountEnabled = false;
        Assert.Equal((false, false, (RaidGate?)null), RaidReadPlan.Of(s, Gates, "act3-g3"));
        Assert.True(s.Gate("act3-g3").CountWindow);   // the gate's own switch is untouched
        s.CountEnabled = true;
        Assert.Equal((false, true, (RaidGate?)Mordum), RaidReadPlan.Of(s, Gates, null));
    }

    [Fact]
    public void The_main_줄_수_switch_off_leaves_the_debuff_window_reading_the_name()
    {
        var s = Count();
        (s.CountEnabled, s.Gate("act3-g3").DebuffWindow) = (false, true);
        Assert.Equal((true, false, (RaidGate?)Mordum), RaidReadPlan.Of(s, Gates, "act3-g3"));
    }

    [Fact]
    public void A_gate_that_always_shows_its_count_still_does_while_another_is_recognised()
    {
        var s = new RaidSettings();
        (s.Gate("act3-g3").CountWindow, s.Gate("act3-g3").CountAuto) = (true, false);
        Assert.Equal((false, true, (RaidGate?)Mordum), RaidReadPlan.Of(s, Gates, "act3-g2"));
    }
}
