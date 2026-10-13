using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>▶ 실행해 보기 plays the debuff and 단심 windows too (user, 2026-10-13), on loops; the 번개 box cycles between the
/// lightning and the 결투자, which share it.</summary>
public class PreviewSimTests
{
    static readonly TimeSpan T0 = TimeSpan.FromSeconds(1000);
    static TimeSpan At(double s) => T0 + TimeSpan.FromSeconds(s);

    static Snapshot Sample(bool debuffs = true, bool judgment = true) => Snapshot.Empty(T0) with
    {
        Debuffs = debuffs ? DebuffStatus.None : null,
        Judgment = judgment ? JudgmentState.Ready : null,
    };

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
    [InlineData(33, JudgmentPhase.Ready)]
    [InlineData(40, JudgmentPhase.Active)]   // the next loop
    public void 단심_plays_its_cycle(double at, JudgmentPhase phase)
    {
        Assert.Equal(phase, new PreviewSim(Sample(), T0).At(At(at)).Judgment!.Phase(At(at)));
    }

    [Fact]
    public void 단심_flashes_when_ready()
    {
        var s = new PreviewSim(Sample(), T0).At(At(32.1)).Judgment!;
        Assert.True(s.Icon(At(32.1), showCooldown: false)?.Flash);
    }

    [Fact]
    public void Windows_not_in_the_sample_stay_off()
    {
        var s = new PreviewSim(Sample(debuffs: false, judgment: false), T0).At(At(7));
        Assert.Null(s.Debuffs);
        Assert.Null(s.Judgment);
    }
}
