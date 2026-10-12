using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>When the overlay windows are on screen: 오버레이 표시/숨기기 (hotkey, tray, 설정) is the user's switch, and it
/// holds while 설정 is open too.</summary>
public class OverlayVisibilityTests
{
    static bool Show(bool edit = false, bool preview = false, bool visible = true, bool game = true, bool hud = true, bool firstRun = true) =>
        OverlayVisibility.Show(edit, preview, visible, game, hud, firstRun);

    [Fact]
    public void Hiding_the_overlay_works_while_settings_is_open()
    {
        Assert.False(Show(preview: true, visible: false));
        Assert.False(Show(preview: true, visible: false, game: false));
    }

    [Fact]
    public void Settings_shows_the_preview_whatever_is_in_front_while_the_overlay_is_on()
    {
        Assert.True(Show(preview: true, game: false));
        Assert.True(Show(preview: true, hud: false));
        Assert.True(Show(preview: true, firstRun: false));
    }

    [Fact]
    public void Edit_mode_always_shows_the_overlay()
    {
        Assert.True(Show(edit: true, visible: false));
        Assert.True(Show(edit: true, visible: false, game: false, hud: false, firstRun: false));
    }

    [Fact]
    public void Without_settings_the_overlay_shows_only_with_the_game_in_front_the_hud_up_and_the_first_run_done()
    {
        Assert.True(Show());
        Assert.False(Show(visible: false));
        Assert.False(Show(game: false));
        Assert.False(Show(hud: false));
        Assert.False(Show(firstRun: false));
    }
}
