using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>테스트 모드's fits: found on the video in front and kept in memory only, so a video at another resolution or HUD
/// size never uses (or overwrites) the game's own saved fit.</summary>
public class TestFitsTests
{
    static readonly Viewport Wide = new(0, 135, 1920, 810);

    [Fact]
    public void A_fit_is_kept_per_game_area_until_cleared()
    {
        var fits = new TestFits();
        Assert.Null(fits.For(Fhd));
        fits.Set(Fhd, new Fit(80));
        Assert.Equal(new Fit(80), fits.For(Fhd));
        Assert.Null(fits.For(Wide));
        fits.Clear();
        Assert.Null(fits.For(Fhd));
    }

    [Fact]
    public void A_fit_is_tried_at_once_then_every_two_seconds()
    {
        var fits = new TestFits();
        Assert.True(fits.ShouldTry(Fhd, S(10)));
        Assert.False(fits.ShouldTry(Fhd, S(11)));
        Assert.True(fits.ShouldTry(Fhd, S(12)));
    }

    [Fact]
    public void Another_game_area_or_a_clear_is_tried_at_once()
    {
        var fits = new TestFits();
        Assert.True(fits.ShouldTry(Fhd, S(10)));
        Assert.True(fits.ShouldTry(Wide, S(10.5)));   // the video went to 21:9
        fits.Clear();
        Assert.True(fits.ShouldTry(Wide, S(10.6)));   // test mode switched off and on
    }

    [Fact]
    public void Two_areas_flickering_are_each_tried_every_two_seconds()
    {
        var fits = new TestFits();
        Assert.True(fits.ShouldTry(Fhd, S(10)));
        Assert.True(fits.ShouldTry(Wide, S(10.2)));
        Assert.False(fits.ShouldTry(Fhd, S(10.4)));    // back to the first: tried 0.4 s ago
        Assert.False(fits.ShouldTry(Wide, S(10.6)));
        Assert.True(fits.ShouldTry(Fhd, S(12)));
    }

    [Fact]
    public void An_area_with_a_fit_is_not_tried_again()
    {
        var fits = new TestFits();
        fits.Set(Fhd, new Fit(100));
        Assert.False(fits.ShouldTry(Fhd, S(100)));
    }

    [Fact]
    public void Describe_shows_HUD_and_screen_pixels() =>
        Assert.Equal("자동 맞춤 82.5% · +3, −1 px", TestFits.Describe(new Fit(82.5, 3 / 0.9, -1 / 0.9), 0.9));

    [Fact]
    public void Describe_shows_zero_offsets() => Assert.Equal("자동 맞춤 90% · +0, +0 px", TestFits.Describe(new Fit(90), 1));

    [Fact]
    public void Offsets_is_Describe_without_its_label() => Assert.Equal("82.5% · +3, −1 px", TestFits.Offsets(new Fit(82.5, 3 / 0.9, -1 / 0.9), 0.9));

    [Fact]
    public void Revert_puts_back_the_fit_before()
    {
        var fits = new TestFits();
        fits.SetFound(Fhd, new Fit(82.5, 3), new Fit(82));
        Assert.Equal(new Fit(82), fits.Revert(Fhd));
        Assert.Equal(new Fit(82), fits.For(Fhd));
        Assert.Null(fits.Revert(Fhd));   // once
    }

    [Fact]
    public void Revert_without_a_before_keeps_the_fit()
    {
        var fits = new TestFits();
        fits.SetFound(Fhd, new Fit(82.5), null);
        Assert.Null(fits.Revert(Fhd));
        Assert.Equal(new Fit(82.5), fits.For(Fhd));
    }

    [Fact]
    public void Clear_drops_the_kept_before()
    {
        var fits = new TestFits();
        fits.SetFound(Fhd, new Fit(82.5), new Fit(82));
        fits.Clear();
        Assert.Null(fits.Revert(Fhd));
    }

    [Fact]
    public void A_later_set_drops_the_kept_before()
    {
        var fits = new TestFits();
        fits.SetFound(Fhd, new Fit(82.5), new Fit(82));
        fits.Set(Fhd, new Fit(90));   // 위치 조정's 적용
        Assert.Null(fits.Revert(Fhd));
    }

    [Fact]
    public void A_moved_window_keeps_its_fit_the_fit_is_per_picture_size()
    {
        var fits = new TestFits();
        fits.Set(new Viewport(100, 80, 1280, 720), new Fit(90));
        Assert.Equal(new Fit(90), fits.For(new Viewport(400, 300, 1280, 720)));
        Assert.False(fits.ShouldTry(new Viewport(400, 300, 1280, 720), S(1)));
    }

    static readonly (int, int) Window = (1300, 800);
    static readonly PixelRect Inside = new(10, 40, 1280, 720);

    [Fact]
    public void A_picture_is_kept_per_window_size_relative_to_the_window()
    {
        var fits = new TestFits();
        Assert.Null(fits.Picture(Window));
        fits.SetPicture(Window, Inside, locked: false);
        Assert.Equal((Inside, false), fits.Picture(Window));
        Assert.Null(fits.Picture((1920, 1080)));
        fits.SetPicture(Window, Inside, locked: true);
        Assert.Equal((Inside, true), fits.Picture(Window));
        Assert.Equal(new Viewport(110, 140, 1280, 720), TestFits.Area(new PixelRect(100, 100, 1300, 800), Inside));
    }

    [Fact]
    public void The_picture_is_looked_for_at_once_then_every_two_seconds_until_dropped_or_cleared()
    {
        var fits = new TestFits();
        Assert.True(fits.ShouldFind(S(10)));
        Assert.False(fits.ShouldFind(S(11)));   // also for another size: a window being resized is looked at every 2 s
        Assert.True(fits.ShouldFind(S(12)));
        fits.SetPicture(Window, Inside, locked: true);
        fits.DropPicture(Window);   // 스킬창 맞추기 looks again
        Assert.Null(fits.Picture(Window));
        Assert.True(fits.ShouldFind(S(12.5)));
        fits.SetPicture(Window, Inside, locked: true);
        fits.Clear();
        Assert.Null(fits.Picture(Window));
        Assert.True(fits.ShouldFind(S(13)));
    }

    [Fact]
    public void Locking_a_picture_keeps_where_it_is()
    {
        var fits = new TestFits();
        fits.Lock(Window);   // nothing to lock
        Assert.Null(fits.Picture(Window));
        fits.SetPicture(Window, Inside, locked: false);
        fits.Lock(Window);
        Assert.Equal((Inside, true), fits.Picture(Window));
    }

    [Fact]
    public void The_video_s_buff_size_settles_on_two_finds_in_a_row()
    {
        var fits = new TestFits();
        Assert.False(fits.NoteBuffSize(90));
        Assert.False(fits.NoteBuffSize(100));   // another size starts over
        Assert.True(fits.NoteBuffSize(100));
        Assert.Equal(100, fits.BuffSize);
        Assert.True(fits.BuffSizeKnown);
        fits.Clear();
        Assert.Null(fits.BuffSize);
        Assert.False(fits.BuffSizeKnown);
    }

    [Fact]
    public void A_buff_size_set_by_위치_조정_is_known_and_not_overwritten()
    {
        var fits = new TestFits();
        fits.BuffSize = 110;
        Assert.True(fits.BuffSizeKnown);
        Assert.False(fits.NoteBuffSize(90));
        Assert.False(fits.NoteBuffSize(90));
        Assert.Equal(110, fits.BuffSize);
    }
}
