using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>감지 page placing: 되돌리기 goes back one finished edit at a time, at most <see cref="PlaceHistory.MaxSteps"/>.</summary>
public class PlaceHistoryTests
{
    static readonly Viewport Screen = new(0, 0, 1920, 1080);
    static FitAdjust Session() => FitAdjust.Begin(Screen, 120, new Fit(100));

    /// <summary><paramref name="count"/> moves of the bar, each one step, and the state after each (the first is the session's start).</summary>
    static (PlaceHistory History, List<FitAdjust> States) Moved(int count)
    {
        var (history, states) = (PlaceHistory.Empty, new List<FitAdjust> { Session() });
        for (int i = 0; i < count; i++)
        {
            var next = states[^1].Move(AdjustPart.스킬창, 1, 0);
            history = history.After(states[^1], next);
            states.Add(next);
        }
        return (history, states);
    }

    [Fact]
    public void A_new_history_has_nothing_to_go_back_to()
    {
        Assert.False(PlaceHistory.Empty.CanGoBack);
        Assert.Null(PlaceHistory.Empty.Back());
    }

    [Fact]
    public void Going_back_returns_each_edit_newest_first()
    {
        var (history, states) = Moved(3);

        for (int i = 2; i >= 0; i--)
        {
            var back = history.Back()!.Value;
            Assert.Equal(states[i], back.Step);
            history = back.History;
        }

        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void A_press_that_changed_nothing_is_no_step()
    {
        var a = Session();

        Assert.False(PlaceHistory.Empty.After(a, a).CanGoBack);
    }

    [Fact]
    public void A_drag_that_ends_where_it_started_is_no_step()
    {
        var a = Session();

        var there = a.Move(AdjustPart.버프, 5, 0).Move(AdjustPart.버프, -5, 0);

        Assert.False(PlaceHistory.Empty.After(a, there).CanGoBack);
    }

    [Fact]
    public void Past_the_limit_the_oldest_step_is_forgotten()
    {
        var (history, states) = Moved(PlaceHistory.MaxSteps + 1);

        for (int i = 0; i < PlaceHistory.MaxSteps; i++)
        {
            var back = history.Back()!.Value;
            Assert.Equal(states[PlaceHistory.MaxSteps - i], back.Step);
            history = back.History;
        }

        Assert.False(history.CanGoBack);   // states[0], the 21st back, is gone
    }

    [Fact]
    public void Adding_a_step_leaves_the_history_it_came_from_as_it_was()
    {
        var (history, states) = Moved(1);

        history.After(states[1], states[1].Move(AdjustPart.버프, 3, 0));

        Assert.Equal(states[0], history.Back()!.Value.Step);
        Assert.False(history.Back()!.Value.History.CanGoBack);
    }

    [Fact]
    public void Going_back_over_a_reset_restores_the_moves_it_dropped()
    {
        var (history, states) = Moved(2);
        var reset = states[^1].Reverted();

        var back = history.After(states[^1], reset).Back()!.Value;

        Assert.Equal(states[^1], back.Step);
        Assert.True(back.Step.Changed);
    }
}
