using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

// The box at the top left of 설정's 내 오버레이 canvas (user, 2026-11-01): the screen and the HUD the reads use.
public sealed class ScreenSummaryTests
{
    static Settings Found(int w, int h, bool wide, string key, double hud) =>
        new() { DetectedScreen = new(w, h, wide), Fits = new() { [key] = new Fit(hud) } };

    [Theory]
    [InlineData(1920, 1080, false, "1920×1080", "16:9")]
    [InlineData(2560, 1440, false, "2560×1440", "16:9")]
    [InlineData(2560, 1080, false, "2560×1080", "21:9")]
    [InlineData(3440, 1440, false, "3440×1440", "21:9")]
    [InlineData(1920, 1200, false, "1920×1200", "16:10")]
    [InlineData(1920, 1080, true, "1920×810", "21:9 (강제)")]   // the forced 21:9 bars a 16:9 screen: its fit is the 64:27 area's
    public void The_screen_its_ratio_and_the_fits_hud_are_shown(int w, int h, bool wide, string key, string ratio)
    {
        var facts = ScreenSummary.Of(Found(w, h, wide, key, 110), gameRunning: true);
        Assert.Equal(new ScreenFacts($"{w}×{h}", ratio, "110% (자동)"), facts);
    }

    [Fact]
    public void Without_a_game_window_the_last_screen_is_marked() =>
        Assert.Equal("1920×1080 (마지막)", ScreenSummary.Of(Found(1920, 1080, false, "1920×1080", 100), gameRunning: false).Resolution);

    [Fact]
    public void A_typed_hud_and_screen_win_over_what_was_found()
    {
        var s = Found(1920, 1080, false, "1920×1080", 100);
        (s.ManualHud, s.ManualScreen) = (120, new(2560, 1440, false));
        Assert.Equal(new ScreenFacts("2560×1440", "16:9", "120% (직접)"), ScreenSummary.Of(s, gameRunning: true));
    }

    [Fact]
    public void An_applied_hand_adjustment_is_named()
    {
        var s = Found(1920, 1080, false, "1920×1080", 100);
        s.Adjusts["1920×1080"] = new Fit(105);
        s.UseAdjusts = true;
        Assert.Equal("105% (수동 조정)", ScreenSummary.Of(s, gameRunning: true).Hud);
    }

    [Fact]
    public void A_hand_adjustment_wins_over_typed_values_kept_from_before()   // 수동 조정 keeps 직접 입력's values (SetFitMode)
    {
        var s = Found(1920, 1080, false, "1920×1080", 100);
        (s.ManualHud, s.ManualScreen) = (110, new(1920, 1080, false));
        s.Adjusts["1920×1080"] = new Fit(117);
        s.UseAdjusts = true;
        Assert.Equal("117% (수동 조정)", ScreenSummary.Of(s, gameRunning: true).Hud);
    }

    [Fact]
    public void A_typed_screen_as_wide_as_the_forced_ratio_is_not_forced() =>   // the game follows such a screen: no bars
        Assert.Equal("21:9", ScreenSummary.Ratio(new(2560, 1080, Wide: true)));

    [Fact]
    public void Nothing_found_yet_shows_dashes() =>
        Assert.Equal(new ScreenFacts("—", "—", "—"), ScreenSummary.Of(new Settings(), gameRunning: true));
}
