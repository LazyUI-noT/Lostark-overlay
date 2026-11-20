using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class ChangeRecorderTests
{
    sealed class Clock { public DateTime Now = new(2026, 1, 1); public void Pass(double s) => Now = Now.AddSeconds(s); }

    static (ChangeRecorder rec, SettingsHistory h, Clock clock, string[] state) Make()
    {
        var h = new SettingsHistory();
        var clock = new Clock();
        var state = new[] { "A" };
        return (new ChangeRecorder(h, () => state[0], () => clock.Now), h, clock, state);
    }

    [Fact]
    public void A_paused_drag_is_one_step()
    {
        var (rec, h, clock, state) = Make();
        rec.Reset();
        rec.BeginGesture();
        state[0] = "B"; rec.Change();
        clock.Pass(1);   // longer than the 0.6 s merge window
        state[0] = "C"; rec.Change();
        rec.EndGesture();
        Assert.Equal("A", h.Undo("C"));
        Assert.False(h.CanUndo);
        Assert.Equal(1, h.RedoCount);
    }

    [Fact]
    public void A_release_outside_a_gesture_does_not_merge_the_next_click_into_the_last_step()
    {
        var (rec, h, clock, state) = Make();
        rec.Reset();
        state[0] = "B"; rec.Change();
        clock.Pass(1);
        rec.EndGesture();   // a mouse-up on a check box: no gesture was open
        state[0] = "C"; rec.Change();
        Assert.Equal(2, h.UndoCount);
    }

    [Fact]
    public void A_step_taken_during_a_gesture_ends_it()
    {
        var (rec, h, _, state) = Make();
        rec.Reset();
        rec.BeginGesture();
        state[0] = "B"; rec.Change();
        rec.Separate();   // an undo mid-drag
        state[0] = "C"; rec.Change();
        Assert.Equal(2, h.UndoCount);
    }

    [Fact]
    public void A_press_without_a_change_is_no_step()
    {
        var (rec, h, _, _) = Make();
        rec.Reset();
        rec.BeginGesture();
        rec.EndGesture();
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Without_a_gesture_changes_a_pause_apart_are_two_steps_and_close_ones_one()
    {
        var (rec, h, clock, state) = Make();
        rec.Reset();
        state[0] = "B"; rec.Change();
        clock.Pass(0.1);
        state[0] = "C"; rec.Change();   // merged with the first
        clock.Pass(1);
        state[0] = "D"; rec.Change();
        Assert.Equal(2, h.UndoCount);
    }

    [Fact]
    public void Two_gestures_are_two_steps_and_undo_then_redo_round_trips()
    {
        var (rec, h, clock, state) = Make();
        rec.Reset();
        rec.BeginGesture(); state[0] = "B"; rec.Change(); rec.EndGesture();
        clock.Pass(0.1);   // right after: still its own step
        rec.BeginGesture(); state[0] = "C"; rec.Change(); rec.EndGesture();
        Assert.Equal(2, h.UndoCount);
        Assert.Equal("B", h.Undo("C"));
        Assert.Equal("A", h.Undo("B"));
        Assert.Equal(2, h.RedoCount);
        Assert.Equal("B", h.Redo("A"));
        Assert.Equal("C", h.Redo("B"));
        Assert.Equal(2, h.UndoCount);
        Assert.Equal(0, h.RedoCount);
    }

    [Fact]
    public void Counts_follow_record_undo_redo_and_clear()
    {
        var h = new SettingsHistory();
        Assert.Equal((0, 0), (h.UndoCount, h.RedoCount));
        h.Record("A"); h.Record("B");
        Assert.Equal((2, 0), (h.UndoCount, h.RedoCount));
        h.Undo("C");
        Assert.Equal((1, 1), (h.UndoCount, h.RedoCount));
        h.Clear();
        Assert.Equal((0, 0), (h.UndoCount, h.RedoCount));
    }
}
