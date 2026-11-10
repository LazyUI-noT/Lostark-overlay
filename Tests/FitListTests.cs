using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

// 스킬창 맞추기's list (user, 2026-11-10): one row for this PC's game screen, one for 테스트 모드, the one in use marked.
public sealed class FitListTests
{
    static Settings Saved() => new()
    {
        DetectedScreen = new(1920, 1080, false),
        Fits = new() { ["1920×1080"] = new Fit(110), ["1920×1040"] = new Fit(57.5), ["1920×810"] = new Fit(110.2) },
    };

    [Fact]
    public void Only_the_game_row_and_the_test_row_are_listed()
    {
        var rows = FitList.Rows(Saved(), areaKey: "1920×1080", testMode: false, testHud: null);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new FitRow("게임 1920×1080", "HUD 110%", InUse: true), rows[0]);
        Assert.False(rows[1].InUse);
    }

    [Fact]
    public void The_game_row_follows_the_area_in_use()
    {
        var rows = FitList.Rows(Saved(), areaKey: "1920×1040", testMode: false, testHud: null);

        Assert.Equal(new FitRow("게임 1920×1040", "HUD 57.5%", InUse: true), rows[0]);
    }

    [Fact]
    public void Without_a_game_window_the_detected_screen_is_shown_not_in_use()
    {
        var rows = FitList.Rows(Saved(), areaKey: null, testMode: false, testHud: null);

        Assert.Equal(new FitRow("게임 1920×1080", "HUD 110%", InUse: false), rows[0]);
    }

    [Fact]
    public void In_test_mode_the_test_row_is_in_use_with_the_videos_hud()
    {
        var rows = FitList.Rows(Saved(), areaKey: "1280×720", testMode: true, testHud: 95);

        Assert.Equal(new FitRow("게임 1920×1080", "HUD 110%", InUse: false), rows[0]);
        Assert.Equal(new FitRow("테스트 1280×720", "HUD 95%", InUse: true), rows[1]);
    }

    [Fact]
    public void A_test_area_without_a_fit_yet_says_so() =>
        Assert.Equal("찾는 중", FitList.Rows(Saved(), "1280×720", testMode: true, testHud: null)[1].Value);

    [Fact]
    public void An_adjusted_fit_is_shown_beside_the_found_one()
    {
        var s = Saved();
        s.Adjusts["1920×1080"] = new Fit(108);

        Assert.Equal("HUD 110% · 수동 조정 HUD 108%", FitList.Rows(s, "1920×1080", false, null)[0].Value);
    }

    [Fact]
    public void Never_fitted_says_so()
    {
        var rows = FitList.Rows(new Settings(), areaKey: null, testMode: false, testHud: null);

        Assert.Equal(new FitRow("게임", "아직 맞춘 적이 없습니다", InUse: false), rows[0]);
    }
}
