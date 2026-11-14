using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The 채널 box's arrow (channel-cue.plan.md): gold in town and field, grey in instanced content. Fixtures chan_*: the
/// game area's top-right corner (Geometry.ChannelCorner); which frames are town is by eye on the frames. Measured: town 25–32
/// warm pixels at 1080 scale in every recording (1080, 21:9, 4K, 4K forced 21:9), everything else 0.</summary>
public class ChannelBoxTests
{
    [Theory]
    [InlineData("chan_town", 1.0, 23)]    // town, then a dark screen from frame 23
    [InlineData("chan_wide1", 1.0, 3)]    // 21:9: town, then the loading screen and a dungeon
    [InlineData("chan_wide2", 1.0, 0)]    // a loading into a dungeon, a white flash on the way
    [InlineData("chan_raid", 1.0, 0)]     // a whole raid: "-----" and a grey arrow
    [InlineData("chan_user", 1.0, 1)]
    [InlineData("chan_4k", 2.0, 1)]
    [InlineData("chan_4k219", 1.5, 1)]
    public void Only_town_frames_show_the_gold_arrow(string name, double scale, int townFrames)
    {
        var frames = Fixture.Load(name).Frames;
        var seen = frames.Select((f, i) => (f, i)).Where(p => ChannelBox.IsOutside(p.f, scale)).Select(p => p.i).ToArray();
        Assert.Equal(Enumerable.Range(0, townFrames).ToArray(), seen);
    }

    [Fact]
    public void A_black_or_empty_corner_says_nothing()
    {
        Assert.False(ChannelBox.IsOutside(new Image(110, 40), 1));
        Assert.False(ChannelBox.IsOutside(new Image(0, 0), 1));
    }

    [Fact]
    public void A_warm_flood_is_no_arrow()
    {
        var bgra = new byte[110 * 40 * 4];
        for (int i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (148, 204, 220, 255);
        Assert.False(ChannelBox.IsOutside(new Image(110, 40, bgra), 1));
    }

    // Sparks or a gold effect behind the semi-transparent box: enough warm pixels, spread thin over an arrow-sized area.
    [Fact]
    public void Scattered_warm_pixels_are_no_arrow()
    {
        var bgra = new byte[110 * 40 * 4];
        // 25 pixels over 13 × 13: count and spans pass, only the fill (0.15, the arrow's is 0.31–0.38) tells it apart
        for (int y = 10; y <= 22; y += 3)
            for (int x = 45; x <= 57; x += 3)
            {
                int i = (y * 110 + x) * 4;
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (148, 204, 220, 255);
            }
        Assert.False(ChannelBox.IsOutside(new Image(110, 40, bgra), 1));
    }

    [Fact]
    public void Gold_reads_count_in_a_row_a_second_apart()
    {
        static TimeSpan s(int sec) => TimeSpan.FromSeconds(sec);
        Assert.Equal(1, ChannelBox.Hits(0, null, outside: true, s(10)));
        Assert.Equal(2, ChannelBox.Hits(1, s(10), outside: true, s(11)));
        Assert.Equal(0, ChannelBox.Hits(1, s(10), outside: false, s(11)));
    }

    // In town, then a loading (no reads) into a raid: the first gold-looking read there starts over, it ends no stay.
    [Fact]
    public void A_gap_in_the_reads_starts_the_count_over() =>
        Assert.Equal(1, ChannelBox.Hits(5, TimeSpan.FromSeconds(10), outside: true, TimeSpan.FromSeconds(10) + ChannelBox.ReadGap + TimeSpan.FromSeconds(0.1)));

    // The game area's top-right corner, 110×40 at 1080 scale: no in-game size moves it (user, 2026-11-14).
    [Theory]
    [InlineData(0, 0, 1920, 1080, 1810, 0, 110, 40)]
    [InlineData(0, 180, 2560, 1080, 2450, 180, 110, 40)]   // 21:9 in a 1440 recording
    [InlineData(0, 0, 3840, 2160, 3620, 0, 220, 80)]
    [InlineData(0, 270, 3840, 1620, 3675, 270, 165, 60)]   // 4K forced 21:9
    public void The_corner_follows_the_game_area(int x, int y, int w, int h, int rx, int ry, int rw, int rh) =>
        Assert.Equal(new PixelRect(rx, ry, rw, rh), Geometry.ChannelCorner(new Viewport(x, y, w, h)));
}
