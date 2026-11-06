using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>표시 화면: which display the overlay windows stand on, and where a window lands on it.</summary>
public class DisplayMapTests
{
    static readonly DisplayArea Main = new("A", 0, 0, 1920, 1080, true);
    static readonly DisplayArea Side = new("B", 1920, 0, 2560, 1440, false);
    static readonly DisplayArea[] Both = [Main, Side];

    [Fact]
    public void Empty_setting_moves_nothing() => Assert.Null(DisplayMap.Target("", Both, "B"));

    [Fact]
    public void Game_setting_follows_the_games_display() => Assert.Equal(Side, DisplayMap.Target(DisplayMap.GameDisplay, Both, "B"));

    [Fact]
    public void Game_setting_with_no_game_running_moves_nothing() => Assert.Null(DisplayMap.Target(DisplayMap.GameDisplay, Both, null));

    [Fact]
    public void A_named_display_wins_over_the_games() => Assert.Equal(Main, DisplayMap.Target("A", Both, "B"));

    [Fact]
    public void A_display_gone_falls_back_to_the_games_then_the_primary()
    {
        Assert.Equal(Side, DisplayMap.Target("C", Both, "B"));
        Assert.Equal(Main, DisplayMap.Target("C", Both, null));
    }

    [Fact]
    public void A_window_already_on_the_target_stays() =>
        Assert.Equal((2000.0, 100.0), DisplayMap.Into(2000, 100, 300, 200, Both, Side));

    [Fact]
    public void A_window_keeps_its_share_of_the_display_when_moved() =>
        Assert.Equal((1920.0 + 1280, 720.0), DisplayMap.Into(960, 540, 100, 100, Both, Side));

    [Fact]
    public void A_moved_window_stays_inside_the_target() =>
        Assert.Equal((1920.0 - 500, 1080.0 - 300), DisplayMap.Into(4400, 1400, 500, 300, Both, Main));

    [Fact]
    public void A_default_spot_stays_default()
    {
        var (l, t) = DisplayMap.Into(double.NaN, double.NaN, 100, 100, Both, Side);
        Assert.True(double.IsNaN(l) && double.IsNaN(t));
    }

    [Fact]
    public void A_window_on_no_display_counts_as_on_the_primary()
    {
        var (l, t) = DisplayMap.Into(-500, -500, 10, 10, Both, Side);
        Assert.Equal((1920.0, 0.0), (l, t));   // its share of the primary is negative: clamped inside the target
    }
}
