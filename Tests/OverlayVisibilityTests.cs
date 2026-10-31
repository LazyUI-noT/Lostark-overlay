using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>When the overlay windows are on screen: 오버레이 표시 (끄기 / 켜기 / 항상: hotkey, tray, 설정) is the user's switch,
/// and 설정 opening changes nothing but counting as "in front" like the game.</summary>
public class OverlayVisibilityTests
{
    static bool Show(bool edit = false, OverlayShow mode = OverlayShow.켜기, bool own = false, bool game = true, bool hud = true,
        bool hideWithoutHud = false, bool firstRun = true) =>
        OverlayVisibility.Show(edit, mode, own, game, hud, hideWithoutHud, firstRun);

    [Fact]
    public void 끄기_hides_everywhere_also_with_settings_in_front()
    {
        Assert.False(Show(mode: OverlayShow.끄기));
        Assert.False(Show(mode: OverlayShow.끄기, own: true, game: false));
    }

    [Fact]
    public void 켜기_shows_with_the_game_or_settings_in_front_whether_or_not_the_game_runs()
    {
        Assert.True(Show());
        Assert.True(Show(own: true, game: false));
        Assert.True(Show(own: true, game: false, firstRun: false));
        Assert.False(Show(game: false));   // another app in front
    }

    [Fact]
    public void 항상_shows_over_any_app()
    {
        Assert.True(Show(mode: OverlayShow.항상, game: false));
        Assert.False(Show(mode: OverlayShow.항상, game: false, firstRun: false));   // not over the first-run window's own steps
    }

    [Fact]
    public void A_hidden_skill_bar_hides_only_when_asked_and_only_with_the_game_in_front()
    {
        Assert.True(Show(hud: false));
        Assert.False(Show(hud: false, hideWithoutHud: true));
        Assert.True(Show(hud: false, hideWithoutHud: true, own: true, game: false));   // settings in front: the HUD isn't looked at
        Assert.True(Show(mode: OverlayShow.항상, hud: false, hideWithoutHud: true, game: false));
    }

    [Fact]
    public void The_game_in_front_waits_for_the_first_run()
    {
        Assert.False(Show(firstRun: false));
    }

    [Fact]
    public void Edit_mode_always_shows_the_overlay() =>
        Assert.True(Show(edit: true, mode: OverlayShow.끄기, game: false, hud: false, hideWithoutHud: true, firstRun: false));

    [Fact]
    public void The_mode_is_kept_as_Visible_and_Always_so_older_files_still_read()
    {
        var o = new OverlaySettings();
        Assert.Equal(OverlayShow.켜기, o.Mode());
        o.SetMode(OverlayShow.항상);
        Assert.Equal((true, true), (o.Visible, o.Always));
        o.Visible = false;   // the hotkey's toggle
        Assert.Equal(OverlayShow.끄기, o.Mode());
        o.Visible = true;
        Assert.Equal(OverlayShow.항상, o.Mode());   // back to the on-mode it was in
        o.SetMode(OverlayShow.켜기);
        Assert.Equal((true, false), (o.Visible, o.Always));
    }
}
