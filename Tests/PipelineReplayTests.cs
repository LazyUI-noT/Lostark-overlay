using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>L1 → L0 together on the recorded frames: vision readings drive the tracker exactly as the engine does,
/// and the tracker's predictions are checked against what the game showed.</summary>
public class PipelineReplayTests
{
    const double Frame = 1 / 60.0;

    static (List<SlotState> States, Fixture F) Replay(string name, SlotId slot, int calibrateFrom = -1)
    {
        var f = Fixture.Load(name);
        var digits = new DigitReader(Geometry.Scale(Fhd, new Fit(f.Hud)));
        var cal = calibrateFrom >= 0 ? new SlotCalibration(f.Frames[calibrateFrom]) : null;
        var state = SlotState.Unknown(slot);
        var states = new List<SlotState>();
        for (int i = 0; i < f.Frames.Count; i++)
        {
            state = CooldownTracker.Update(state, SlotClassifier.Read(slot, f.Frames[i], f.Pts[i], cal, digits));
            states.Add(state);
        }
        return (states, f);
    }

    [Theory]
    [InlineData("gameplay_D_end", SlotId.일반7, 39, 99)]
    [InlineData("gameplay_S_end", SlotId.일반6, 7, 67)]
    public void Ready_is_predicted_to_the_frame_and_confirmed_on_it(string name, SlotId slot, int edgeFrame, int readyFrame)
    {
        var (states, f) = Replay(name, slot);
        var actualReady = f.Pts[readyFrame];

        // Once the 2s→1s edge is seen, the predicted ready time is within one frame of the real one.
        var lockedEnd = states[edgeFrame].EndsAt!.Value;
        Assert.InRange((lockedEnd - actualReady).TotalSeconds, -Frame, Frame);

        // The slot never shows Ready before the game does, and is Ready on the game's ready frame.
        Assert.All(states.Take(readyFrame), s => Assert.Equal(SlotPhase.Cooldown, s.Phase));
        Assert.Equal(SlotPhase.Ready, states[readyFrame].Phase);
    }

    [Fact]
    public void Cast_highlight_and_red_border_never_break_the_cooldown()
    {
        var (states, _) = Replay("gameplay_D_start", SlotId.일반7);
        Assert.All(states.Skip(1), s => Assert.Equal(SlotPhase.Cooldown, s.Phase));
    }

    [Fact]
    public void Arkpassive_333_reset_lands_on_the_cast_frame()
    {
        var (e, _) = Replay("333_E", SlotId.일반3);
        var (c, _) = Replay("333_C", SlotId.아덴2);
        Assert.Equal(SlotPhase.Cooldown, c[20].Phase);
        Assert.Equal(SlotPhase.Ready, c[21].Phase);       // C reset…
        Assert.Equal(SlotPhase.Cooldown, e[21].Phase);    // …on the frame E's cooldown appears
        Assert.Equal(SlotPhase.Ready, e[19].Phase);
    }

    [Fact]
    public void Hidden_hud_keeps_the_last_state_as_estimated_and_recovers()
    {
        var (states, _) = Replay("gameplay_S_cutscene", SlotId.일반6, calibrateFrom: 0);
        Assert.All(states.Skip(60).Take(160), s => Assert.True(s.Estimated, "HUD hidden → estimated"));
        Assert.False(states[^1].Estimated);
        Assert.NotEqual(SlotPhase.Unknown, states[^1].Phase);
    }
}
