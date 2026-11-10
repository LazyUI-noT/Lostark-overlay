using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>창 밖에서도 읽기: reads go on with another app in front only while the game can be proved on screen. A video
/// alt-tabbed to, over the game, must never be read as a fight.</summary>
public class BackgroundReadTests
{
    static readonly PixelRect Game = new(1920, 0, 2560, 1440);

    [Fact]
    public void A_window_on_another_monitor_does_not_cover_the_game() =>
        Assert.False(BackgroundRead.Covers(Game, new(0, 0, 1920, 1080)));

    [Fact]
    public void A_maximised_neighbour_overhanging_by_its_frame_does_not_cover_the_game() =>
        Assert.False(BackgroundRead.Covers(Game, new(-8, -8, 1936, 1096)));

    [Fact]
    public void A_small_window_over_the_game_covers_it() =>
        Assert.True(BackgroundRead.Covers(Game, new(2000, 100, 300, 200)));

    [Fact]
    public void A_full_screen_window_covers_the_game() =>
        Assert.True(BackgroundRead.Covers(Game, Game));

    [Theory]
    [InlineData(false, false, GameView.Clear)]
    [InlineData(false, true, GameView.Covered)]
    [InlineData(true, false, GameView.Minimized)]
    public void Without_another_app_in_front_it_reads_as_it_always_did(bool readBehind, bool test, GameView view) =>
        Assert.True(BackgroundRead.MayRead(readBehind, otherAppFront: false, test, seesBehind: false, view));

    [Fact]
    public void Switch_off_stops_reads_with_another_app_in_front_even_when_the_game_is_clear() =>
        Assert.False(BackgroundRead.MayRead(false, otherAppFront: true, testMode: false, seesBehind: false, GameView.Clear));

    [Fact]
    public void Switch_off_in_test_mode_reads_as_before() =>
        Assert.True(BackgroundRead.MayRead(false, otherAppFront: true, testMode: true, seesBehind: false, GameView.Covered));

    [Fact]
    public void Switch_on_reads_a_clear_game_with_another_app_in_front() =>
        Assert.True(BackgroundRead.MayRead(true, otherAppFront: true, testMode: false, seesBehind: false, GameView.Clear));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Switch_on_never_reads_a_covered_game_the_capture_cannot_see(bool test) =>
        Assert.False(BackgroundRead.MayRead(true, otherAppFront: true, test, seesBehind: false, GameView.Covered));

    [Fact]
    public void Switch_on_reads_a_covered_game_when_the_capture_sees_behind() =>
        Assert.True(BackgroundRead.MayRead(true, otherAppFront: true, testMode: false, seesBehind: true, GameView.Covered));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_minimised_game_is_never_read_behind(bool seesBehind) =>
        Assert.False(BackgroundRead.MayRead(true, otherAppFront: true, testMode: false, seesBehind, GameView.Minimized));

    [Fact]
    public void Overlays_stay_only_when_the_switch_is_on_and_the_game_is_seen()
    {
        Assert.True(BackgroundRead.ShowsBehind(true, false, GameView.Clear));
        Assert.True(BackgroundRead.ShowsBehind(true, true, GameView.Covered));
        Assert.False(BackgroundRead.ShowsBehind(false, true, GameView.Clear));
        Assert.False(BackgroundRead.ShowsBehind(true, false, GameView.Covered));
        Assert.False(BackgroundRead.ShowsBehind(true, true, GameView.Minimized));
    }

    [Fact]
    public void The_status_line_names_what_covers_the_game()
    {
        Assert.Equal("읽고 있습니다", new BehindStatus(true, GameView.Clear, null).Text);
        Assert.Equal("Discord 창이 게임을 덮고 있어 멈췄습니다", new BehindStatus(false, GameView.Covered, "Discord").Text);
        Assert.Equal("게임이 최소화되어 멈췄습니다", new BehindStatus(false, GameView.Minimized, null).Text);
    }

    [Fact]
    public void New_settings_keep_todays_behaviour()
    {
        var s = new Settings();
        Assert.False(s.Screens.ReadBehind);
        Assert.Equal("", s.Screens.Display);
    }
}
