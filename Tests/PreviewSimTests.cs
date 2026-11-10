using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>▶ 실행해 보기 plays the debuff and 단심 windows too (user, 2026-10-13), on loops; the 번개 box cycles between the
/// lightning and the 결투자, which share it.</summary>
public class PreviewSimTests
{
    static readonly TimeSpan T0 = TimeSpan.FromSeconds(1000);
    static TimeSpan At(double s) => T0 + TimeSpan.FromSeconds(s);

    static Snapshot Sample(bool debuffs = true, bool judgment = true, bool amsu = true) => Snapshot.Empty(T0) with
    {
        Debuffs = debuffs ? DebuffStatus.None : null,
        Judgment = judgment ? JudgmentState.Ready : null,
        Amsu = amsu ? AmsuState.None : null,
    };

    [Fact]
    public void Auto_play_uses_skills_and_buffs_in_turn_without_clicks()
    {
        var sim = new PreviewSim(Sample(), T0);
        sim.Auto(At(0), GaugePlay.Plain);
        sim.Auto(At(0), GaugePlay.Plain);   // the same moment twice: one step
        var first = sim.At(At(0.1));
        Assert.Equal(SlotPhase.Cooldown, first.Slots.Single(x => x.Slot == SlotId.일반1).Phase);
        Assert.DoesNotContain(first.Buffs, b => b.Active);
        sim.Auto(At(1), GaugePlay.Plain);   // the next step: a buff
        var next = sim.At(At(1.1));
        Assert.True(next.Buffs.Single(b => b.Kind == BuffKind.공증).Active);
        Assert.Equal(SlotPhase.Ready, next.Slots.Single(x => x.Slot == SlotId.일반2).Phase);
        // A long gap (the page was hidden) doesn't replay every missed step at once.
        sim.Auto(At(600), GaugePlay.Plain);
        Assert.True(sim.At(At(600.1)).Slots.Count(x => x.Phase == SlotPhase.Cooldown) <= 1);
    }

    [Fact]
    public void The_lightning_and_the_결투자_take_turns_in_their_box()
    {
        var sim = new PreviewSim(Sample(), T0);
        var lightning = sim.At(At(7)).Debuffs!;
        Assert.NotNull(lightning.Lightning);
        Assert.Null(lightning.Duelist);
        var duel = sim.At(At(18)).Debuffs!;
        Assert.Null(duel.Lightning);
        Assert.Equal(DuelStage.Countdown, DuelPhase.At(duel.Duelist, At(18)).Stage);
        Assert.Equal(DuelStage.Icon, DuelPhase.At(sim.At(At(25)).Debuffs!.Duelist, At(25)).Stage);
        Assert.NotNull(sim.At(At(37)).Debuffs!.Lightning);   // the loop again
    }

    [Fact]
    public void The_colour_and_the_swap_play_too()
    {
        var sim = new PreviewSim(Sample(), T0);
        Assert.NotNull(sim.At(At(3)).Debuffs!.Colour);
        Assert.NotEqual(sim.At(At(3)).Debuffs!.Colour, sim.At(At(20)).Debuffs!.Colour);   // red, then blue
        Assert.NotNull(sim.At(At(13)).Debuffs!.Swap);
    }

    [Theory]
    [InlineData(1, JudgmentPhase.Ready)]
    [InlineData(4, JudgmentPhase.Active)]
    [InlineData(20, JudgmentPhase.Cooldown)]
    [InlineData(35, JudgmentPhase.Ready)]
    [InlineData(40, JudgmentPhase.Active)]   // the next loop
    public void 단심_plays_its_cycle(double at, JudgmentPhase phase)
    {
        Assert.Equal(phase, new PreviewSim(Sample(), T0).At(At(at)).Judgment!.Phase(At(at)));
    }

    [Theory]
    [InlineData(0.5, null)]
    [InlineData(1, 3.0)]      // 단죄 at 1 s: its 3 s
    [InlineData(2.5, 1.5)]
    [InlineData(4.5, null)]   // 심판 from 4 s
    public void 단죄_plays_3_s_before_심판(double at, double? left)
    {
        var s = new PreviewSim(Sample(), T0).At(At(at)).Judgment!;
        Assert.Equal(left, s.ConvictionLeft(At(at)) is { } l ? Math.Round(l, 3) : null);
    }

    [Fact]
    public void 단심_flashes_when_ready()
    {
        var s = new PreviewSim(Sample(), T0).At(At(34.1)).Judgment!;
        Assert.True(s.Icon(At(34.1))?.Flash);
    }

    [Fact]
    public void Windows_not_in_the_sample_stay_off()
    {
        var s = new PreviewSim(Sample(debuffs: false, judgment: false), T0).At(At(7));
        Assert.Null(s.Debuffs);
        Assert.Null(s.Judgment);
    }

    [Theory]
    [InlineData(0.5, null)]
    [InlineData(5, 16.0)]      // thrown at 1 s: 20 s
    [InlineData(22, null)]     // ran out at 21 s
    [InlineData(29, 16.0)]     // the next loop (24 s)
    public void 암수_plays_a_throw_and_runs_out(double at, double? left)
    {
        var (shown, l, seen) = Amsu.View(new PreviewSim(Sample(), T0).At(At(at)).Amsu!, At(at));
        Assert.Equal(left, shown is null ? null : Math.Round(l, 3));
        Assert.True(shown is null || seen);
    }

    [Fact]
    public void 암수_stays_off_when_not_in_the_sample() =>
        Assert.Null(new PreviewSim(Sample(amsu: false), T0).At(At(5)).Amsu);

    static double? AmsuLeft(PreviewSim sim, double at)
    {
        var (shown, left, _) = Amsu.View(sim.At(At(at)).Amsu!, At(at));
        return shown is null ? null : Math.Round(left, 3);
    }

    [Fact]
    public void A_click_on_the_amsu_window_throws_it_then_leaves_5_s_then_ends_it_then_throws_again()
    {
        var sim = new PreviewSim(Sample(), T0);
        Assert.Null(AmsuLeft(sim, 0));
        sim.Skip(OverlayPart.Amsu, At(0));
        Assert.Equal(20, AmsuLeft(sim, 0));   // full at once, not after the loop's 1 s
        sim.Skip(OverlayPart.Amsu, At(0));
        Assert.Equal(5, AmsuLeft(sim, 0));    // 5 s left, then it drains
        Assert.Equal(3, AmsuLeft(sim, 2));
        sim.Skip(OverlayPart.Amsu, At(2));
        Assert.Null(AmsuLeft(sim, 2));        // gone
        sim.Skip(OverlayPart.Amsu, At(2));
        Assert.Equal(20, AmsuLeft(sim, 2));   // thrown again, no empty click between
    }

    [Fact]
    public void A_click_on_a_box_with_under_1_s_left_ends_it_instead_of_replaying_the_last_second()
    {
        var sim = new PreviewSim(Sample(), T0);
        sim.Click(Part.일반1, At(0));                        // ready → 5 s cooldown
        sim.Click(Part.일반1, At(1));                        // → 1 s left
        sim.Click(Part.일반1, At(1.5));                      // 0.5 s left: ready now
        var slot = sim.At(At(1.5)).Slots.Single(x => x.Slot == SlotId.일반1);
        Assert.Equal(SlotPhase.Ready, slot.Phase);
        Assert.Equal(At(1.5), slot.ChangedAt);              // its ready shine starts now
        sim.Click(Part.일반1, At(1.6));                      // and the next click starts it again
        Assert.Equal(SlotPhase.Cooldown, sim.At(At(1.6)).Slots.Single(x => x.Slot == SlotId.일반1).Phase);
    }

    [Fact]
    public void A_click_on_a_buff_with_under_1_s_left_ends_it()
    {
        var sim = new PreviewSim(Sample(), T0);
        var bar = FreeLayout.FirstBuff + (int)BuffKind.공증;
        sim.Click(bar, At(0));
        sim.Click(bar, At(1));                               // → 1 s left
        sim.Click(bar, At(1.5));                             // 0.5 s left: off now
        Assert.False(sim.At(At(1.5)).Buffs.Single(b => b.Kind == BuffKind.공증).Active);
    }

    [Fact]
    public void A_click_on_a_loop_under_1_s_before_its_next_change_goes_past_it()
    {
        var sim = new PreviewSim(Sample(), T0);
        var now = At(33.5);
        Assert.Equal(JudgmentPhase.Cooldown, sim.At(now).Judgment!.Phase(now));
        sim.Skip(OverlayPart.Judgment, now);                 // 0.5 s to ready: past it, to the loop's start
        Assert.Equal(JudgmentPhase.Ready, sim.At(now).Judgment!.Phase(now));
        Assert.Null(sim.At(now).Judgment!.ConvictionLeft(now));
        sim.Skip(OverlayPart.Judgment, now);                 // the next click: 단죄, its full 3 s
        Assert.Equal(3, sim.At(now).Judgment!.ConvictionLeft(now));
        sim.Skip(OverlayPart.Judgment, now);                 // then 심판
        Assert.Equal(JudgmentPhase.Active, sim.At(now).Judgment!.Phase(now));
    }

    [Fact]
    public void A_click_on_the_judgment_window_triggers_it_and_leaves_the_others_alone()
    {
        var sim = new PreviewSim(Sample(), T0);
        var debuffsBefore = sim.At(At(0)).Debuffs;
        sim.Skip(OverlayPart.Judgment, At(0));
        Assert.NotEqual(JudgmentState.Ready, sim.At(At(0)).Judgment);
        Assert.Equal(debuffsBefore, sim.At(At(0)).Debuffs);
        sim.Skip(OverlayPart.Skills, At(0));   // not a loop: nothing
    }

    [Theory]
    [InlineData(0, 1)]      // to the first phase
    [InlineData(1, 20)]     // on a phase: to the next one
    [InlineData(22, 2)]     // past the last: to the loop's start
    public void To_next_phase_counts_to_the_next_change(double t, double expected) =>
        Assert.Equal(expected, PreviewSim.ToNextPhase(t, [1, 21], 24), 6);
}
