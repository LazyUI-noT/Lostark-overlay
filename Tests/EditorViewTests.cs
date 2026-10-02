using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The layout editor's view of the screenshot (zoom around the cursor, pan, the first view on the skill bar)
/// and the default spot of each overlay window, shared by the in-game windows and the editor.</summary>
public class EditorViewTests
{
    [Fact]
    public void Zooming_keeps_the_point_under_the_cursor_still()
    {
        var v = new EditorView(1, 10, 20);
        var (wx, wy) = v.ToWorld(300, 200);
        v.ZoomAt(300, 200, 2);
        Assert.Equal(2, v.Scale);
        var (vx, vy) = v.ToView(wx, wy);
        Assert.Equal(300, vx, 6);
        Assert.Equal(200, vy, 6);
    }

    [Fact]
    public void Zoom_stays_within_its_limits()
    {
        var v = new EditorView(1, 0, 0);
        for (int i = 0; i < 40; i++) v.ZoomAt(0, 0, 2);
        Assert.Equal(EditorView.MaxScale, v.Scale);
        for (int i = 0; i < 80; i++) v.ZoomAt(0, 0, 0.5);
        Assert.Equal(EditorView.MinScale, v.Scale);
    }

    /// <summary>원래 크기: 100% (a screen DIP is a view DIP, the game's own size), the point in the middle staying put.</summary>
    [Fact]
    public void Actual_size_is_100_percent_around_the_middle()
    {
        var v = new EditorView(0.37, -120, 45);
        var (wx, wy) = v.ToWorld(400, 300);
        v.ActualSize(400, 300);
        Assert.Equal(1, v.Scale);
        var (vx, vy) = v.ToView(wx, wy);
        Assert.Equal(400, vx, 6);
        Assert.Equal(300, vy, 6);
    }

    /// <summary>전체 화면: the view is the screen itself, so the screenshot lies exactly where the game draws.</summary>
    [Fact]
    public void Full_screen_puts_the_screen_exactly_on_the_view() =>
        Assert.Equal((250.0, 80.0), EditorView.FullScreen().ToView(250, 80));

    [Fact]
    public void Panning_moves_the_view_by_the_drag()
    {
        var v = new EditorView(2, 0, 0);
        v.Pan(15, -5);
        Assert.Equal((15.0, -5.0), v.ToView(0, 0));
    }

    /// <summary>The first view shows the lower middle of the screen, where the skill bar is: a third off the left,
    /// right and top.</summary>
    [Fact]
    public void The_first_view_fits_the_lower_middle_of_the_screen()
    {
        var v = EditorView.Initial(1000, 500, 1920, 1080);
        var (left, top) = v.ToWorld(0, 0);
        var (right, bottom) = v.ToWorld(1000, 500);
        Assert.True(left <= 1920 * 0.33 + 1 && right >= 1920 * 0.67 - 1);
        Assert.True(top <= 1080 * 0.33 + 1 && bottom >= 1080 - 1);
        Assert.True(right - left < 1920 && bottom - top < 1080);   // zoomed in
        Assert.Equal(960, (left + right) / 2, 3);
    }

    [Fact]
    public void Your_overlay_defaults_to_centred_above_the_bottom()
    {
        var at = WindowDefaults.Place(0, 1920, 1040, 400, 100, peer: -1, buffs: false);
        Assert.Equal((760.0, 1040 - 100 - WindowDefaults.AboveBottom), at);
    }

    [Fact]
    public void Party_windows_stack_right_of_centre_and_the_buff_window_sits_under_yours()
    {
        var first = WindowDefaults.Place(0, 1920, 1040, 200, 80, peer: 0, buffs: false);
        var last = WindowDefaults.Place(0, 1920, 1040, 200, 80, peer: 2, buffs: false);
        Assert.Equal(960 + WindowDefaults.PeerOffsetX, first.Left);
        Assert.Equal(2 * WindowDefaults.PeerRow, last.Top - first.Top);
        Assert.Equal(1040 - WindowDefaults.AboveBottom - WindowDefaults.PeerRow, last.Top);   // as before 2.0
        var buffs = WindowDefaults.Place(0, 1920, 1040, 200, 30, peer: -1, buffs: true);
        Assert.Equal(1040 - WindowDefaults.AboveBottom + WindowDefaults.BuffsBelow, buffs.Top);
    }
}
