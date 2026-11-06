using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

// 값 직접 조정 (user, 2026-11-05): the % sizes pick from the game's presets; with it on, − + step by 1% within FitAdjust's ranges.
public sealed class GameSizesTests
{
    [Fact]
    public void Presets_are_what_the_game_offers()
    {
        Assert.Equal(Settings.HudSizes, GameSizes.Presets(GameSize.Hud));
        Assert.Equal(Settings.BuffSizes, GameSizes.Presets(GameSize.Buff));
        Assert.Equal(Settings.BuffSizes, GameSizes.Presets(GameSize.Boss));
        Assert.Equal(JudgmentSettings.FontSizes, GameSizes.Presets(GameSize.Font));
    }

    [Theory]
    [InlineData(GameSize.Hud, 50, 250)]
    [InlineData(GameSize.Buff, 50, 200)]
    [InlineData(GameSize.Boss, 50, 200)]
    public void Fine_ranges_are_fit_adjusts(GameSize kind, int min, int max) => Assert.Equal((min, max), GameSizes.Range(kind));

    [Fact]
    public void The_font_has_no_fine_range() => Assert.Null(GameSizes.Range(GameSize.Font));

    [Theory]
    [InlineData(GameSize.Hud, 100, 1, 101)]
    [InlineData(GameSize.Hud, 100, -1, 99)]
    [InlineData(GameSize.Hud, 250, 1, 250)]
    [InlineData(GameSize.Hud, 50, -1, 50)]
    [InlineData(GameSize.Buff, 200, 1, 200)]
    [InlineData(GameSize.Boss, 50, -1, 50)]
    public void Fine_steps_by_one_and_stops_at_the_range(GameSize kind, int value, int direction, int expected) =>
        Assert.Equal(expected, GameSizes.Step(kind, value, direction, fine: true));

    [Theory]
    [InlineData(GameSize.Hud, 100, 1, 105)]
    [InlineData(GameSize.Hud, 100, -1, 90)]
    [InlineData(GameSize.Hud, 110, 1, 110)]
    [InlineData(GameSize.Hud, 80, -1, 80)]
    [InlineData(GameSize.Hud, 107, 1, 110)]   // an off-preset value moves to the next preset on its side
    [InlineData(GameSize.Hud, 107, -1, 105)]
    [InlineData(GameSize.Font, 100, 1, 150)]
    public void Without_fine_it_moves_to_the_next_preset(GameSize kind, int value, int direction, int expected) =>
        Assert.Equal(expected, GameSizes.Step(kind, value, direction, fine: false));

    [Fact]
    public void The_font_steps_by_presets_even_with_fine() => Assert.Equal(150, GameSizes.Step(GameSize.Font, 100, 1, fine: true));

    [Theory]
    [InlineData(GameSize.Hud, 107, 105)]
    [InlineData(GameSize.Hud, 62, 80)]
    [InlineData(GameSize.Hud, 250, 110)]
    [InlineData(GameSize.Buff, 115, 110)]
    [InlineData(GameSize.Boss, 95, 90)]   // a tie takes the smaller
    public void Snap_takes_the_nearest_preset(GameSize kind, int value, int expected) => Assert.Equal(expected, GameSizes.Snap(kind, value));

    [Fact]
    public void Turning_fine_off_snaps_every_size_to_a_preset()
    {
        var s = new Settings { FineSizes = true, ManualHud = 107 };
        (s.Buffs.Size, s.Debuffs.BossIconSize) = (115, 63);
        s.SetFineSizes(false);
        Assert.False(s.FineSizes);
        Assert.Equal((105.0, 110, 80), (s.ManualHud, s.Buffs.Size, s.Debuffs.BossIconSize));
    }

    [Fact]
    public void Turning_fine_off_leaves_an_unset_hud_unset()
    {
        var s = new Settings { FineSizes = true };
        s.SetFineSizes(false);
        Assert.Null(s.ManualHud);
    }

    [Fact]
    public void Turning_fine_on_changes_no_value()
    {
        var s = new Settings { ManualHud = 105 };
        s.SetFineSizes(true);
        Assert.True(s.FineSizes);
        Assert.Equal(105.0, s.ManualHud);
    }

    [Fact]
    public void Fine_is_off_in_a_file_that_has_no_value() => Assert.False(new Settings().FineSizes);

    // The folded boxes of 게임 설정 show their value.
    [Fact]
    public void Folded_boxes_show_screen_hud_and_sizes()
    {
        var s = new Settings { DetectedScreen = new(2560, 1440, false), Fits = new() { ["2560×1440"] = new Fit(110) } };
        (s.Buffs.Size, s.Debuffs.BossIconSize, s.Judgment.FontSize) = (120, 100, 150);
        Assert.Equal("2560×1440 · 16:9", ScreenSummary.Value(s, true, "g-res"));
        Assert.Equal("2560×1440 (마지막) · 16:9", ScreenSummary.Value(s, false, "g-res"));
        Assert.Equal("110% (자동)", ScreenSummary.Value(s, true, "g-hud"));
        Assert.Equal("120%", ScreenSummary.Value(s, true, "g-buff"));
        Assert.Equal("100%", ScreenSummary.Value(s, true, "g-debuff"));
        Assert.Equal("150%", ScreenSummary.Value(s, true, "g-font"));
        Assert.Null(ScreenSummary.Value(s, true, "skill"));
    }

    [Fact]
    public void A_forced_21_9_screen_says_so_and_none_found_is_a_dash()
    {
        var forced = new Settings { DetectedScreen = new(1920, 1080, true) };
        Assert.Equal("1920×1080 · 21:9 (강제)", ScreenSummary.Value(forced, true, "g-res"));
        Assert.Equal("—", ScreenSummary.Value(new Settings(), true, "g-res"));
    }
}
