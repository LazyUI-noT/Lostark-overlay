using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Sizing a window by its outline on 내 오버레이: handles, ratio, limits, and Ctrl+arrows (WindowSizing).</summary>
public class WindowSizingTests
{
    static readonly (double W, double H) Plate = (100, 50);

    [Fact]
    public void A_corner_keeps_the_ratio_of_two_sizes()
    {
        var (w, h) = WindowSizing.Resize(SizeMode.Two, SizeHandle.Corner, (1, 0.5), Plate, 50, 0);
        Assert.Equal(1.5, w);
        Assert.Equal(0.75, h);
    }

    /// <summary>A zero size (an old or hand-edited file) has no ratio to keep: left as it is, never NaN.</summary>
    [Fact]
    public void A_corner_leaves_a_zero_size_alone()
    {
        Assert.Equal((0, 0.5), WindowSizing.Resize(SizeMode.Two, SizeHandle.Corner, (0, 0.5), Plate, 50, 0));
        Assert.Equal((0, 0), WindowSizing.Resize(SizeMode.Two, SizeHandle.Corner, (0, 0), Plate, 50, 0));
    }

    [Fact]
    public void A_corner_follows_the_axis_moved_most()
    {
        var (w, h) = WindowSizing.Resize(SizeMode.Two, SizeHandle.Corner, (1, 1), Plate, 5, -25);
        Assert.Equal(0.5, w);
        Assert.Equal(0.5, h);
    }

    [Fact]
    public void A_corner_stops_when_either_size_hits_its_limit_and_keeps_the_ratio()
    {
        var (w, h) = WindowSizing.Resize(SizeMode.Two, SizeHandle.Corner, (1, 0.5), Plate, 1000, 0);
        Assert.Equal(ClassProfiles.MaxScale, w);
        Assert.Equal(1.0, h);
        (w, h) = WindowSizing.Resize(SizeMode.Two, SizeHandle.Corner, (1, 0.5), Plate, -1000, 0);
        Assert.Equal(0.6, w);
        Assert.Equal(ClassProfiles.MinScale, h);
    }

    [Fact]
    public void The_right_edge_stretches_only_the_width_and_the_bottom_edge_only_the_height()
    {
        Assert.Equal((1.5, 1.0), WindowSizing.Resize(SizeMode.Two, SizeHandle.Right, (1, 1), Plate, 50, 30));
        Assert.Equal((1.0, 1.6), WindowSizing.Resize(SizeMode.Two, SizeHandle.Bottom, (1, 1), Plate, 30, 30));
    }

    [Fact]
    public void An_edge_is_limited_to_the_scale_range()
    {
        Assert.Equal((ClassProfiles.MaxScale, 1.0), WindowSizing.Resize(SizeMode.Two, SizeHandle.Right, (1, 1), Plate, 9999, 0));
        Assert.Equal((1.0, ClassProfiles.MinScale), WindowSizing.Resize(SizeMode.Two, SizeHandle.Bottom, (1, 1), Plate, 0, -9999));
    }

    [Theory]
    [InlineData(SizeHandle.Right)]
    [InlineData(SizeHandle.Bottom)]
    [InlineData(SizeHandle.Corner)]
    public void A_one_size_window_scales_both_whatever_the_handle(SizeHandle handle)
    {
        var (w, h) = WindowSizing.Resize(SizeMode.Uniform, handle, (1, 1), Plate, handle == SizeHandle.Bottom ? 0 : 50, handle == SizeHandle.Bottom ? 25 : 0);
        Assert.Equal(w, h);
        Assert.Equal(1.5, w);
    }

    [Fact]
    public void A_width_only_window_leaves_the_height_alone_and_a_height_only_one_the_width()
    {
        Assert.Equal((1.5, 0.8), WindowSizing.Resize(SizeMode.WidthOnly, SizeHandle.Corner, (1, 0.8), Plate, 50, 0));
        Assert.Equal((0.8, 1.5), WindowSizing.Resize(SizeMode.HeightOnly, SizeHandle.Corner, (0.8, 1), Plate, 0, 25));
    }

    [Fact]
    public void An_empty_plate_changes_nothing()
    {
        Assert.Equal((1.0, 1.0), WindowSizing.Resize(SizeMode.Two, SizeHandle.Corner, (1, 1), (0, 0), 10, 10));
    }

    [Fact]
    public void Arrow_keys_resize_the_width_with_left_right_and_the_height_with_up_down_where_there_are_two_sizes()
    {
        Assert.Equal((1.1, 1.0), WindowSizing.ByKey(SizeMode.Two, (1, 1), Plate, 10, 0));
        Assert.Equal((1.0, 0.8), WindowSizing.ByKey(SizeMode.Two, (1, 1), Plate, 0, -10));
    }

    [Fact]
    public void Arrow_keys_scale_a_one_size_window_uniformly()
    {
        Assert.Equal((0.9, 0.9), WindowSizing.ByKey(SizeMode.Uniform, (1, 1), Plate, -10, 0));
        Assert.Equal((1.2, 1.2), WindowSizing.ByKey(SizeMode.Uniform, (1, 1), Plate, 0, 10));
    }

    [Fact]
    public void Which_windows_have_two_sizes()
    {
        var s = new Settings();
        Assert.Equal(SizeMode.Uniform, WindowSizing.ModeOf(s, SizedWindow.Raid));
        Assert.Equal(SizeMode.Uniform, WindowSizing.ModeOf(s, SizedWindow.Meter));
        s.Judgment.Display = JudgmentDisplay.Bar;
        Assert.Equal(SizeMode.Two, WindowSizing.ModeOf(s, SizedWindow.Judgment));
        s.Judgment.Display = JudgmentDisplay.ReadyIcon;
        Assert.Equal(SizeMode.WidthOnly, WindowSizing.ModeOf(s, SizedWindow.Judgment));
        s.Amsu.Look = AmsuLook.막대;
        Assert.Equal(SizeMode.Two, WindowSizing.ModeOf(s, SizedWindow.Amsu));
        s.Amsu.Look = AmsuLook.세로막대;
        Assert.Equal(SizeMode.Two, WindowSizing.ModeOf(s, SizedWindow.Amsu));
        s.Amsu.Look = AmsuLook.원;
        Assert.Equal(SizeMode.HeightOnly, WindowSizing.ModeOf(s, SizedWindow.Amsu));
    }

    /// <summary>An upright 암수 bar is 막대 길이 (Width) tall: the right edge, which is its drawn width, changes 크기 (Height).</summary>
    [Fact]
    public void An_upright_amsu_bar_swaps_the_screen_axes()
    {
        var s = new Settings();
        s.Amsu.Look = AmsuLook.세로막대;
        Assert.Equal((1.0, 1.5), WindowSizing.Resize(s, SizedWindow.Amsu, SizeHandle.Right, (1, 1), Plate, 50, 0));
        Assert.Equal((1.5, 1.0), WindowSizing.Resize(s, SizedWindow.Amsu, SizeHandle.Bottom, (1, 1), Plate, 0, 25));
        Assert.Equal((1.0, 1.1), WindowSizing.ByKey(s, SizedWindow.Amsu, (1, 1), Plate, 10, 0));
        s.Amsu.Look = AmsuLook.막대;
        Assert.Equal((1.5, 1.0), WindowSizing.Resize(s, SizedWindow.Amsu, SizeHandle.Right, (1, 1), Plate, 50, 0));
    }
}
