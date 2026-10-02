using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class ScreenShotsTests
{
    [Fact]
    public void Each_profile_has_its_own_numbered_file()
    {
        Assert.Equal(Path.Combine("dir", "1.png"), ScreenShots.PathOf("dir", 0));
        Assert.Equal(Path.Combine("dir", "5.png"), ScreenShots.PathOf("dir", OverlayProfile.Count - 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(OverlayProfile.Count)]
    public void A_profile_outside_the_five_has_no_file(int profile) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenShots.PathOf("dir", profile));

    [Theory]
    [InlineData("a.png", true)]
    [InlineData(@"C:\x\b.JPG", true)]
    [InlineData("c.jpeg", true)]
    [InlineData("d.bmp", true)]
    [InlineData("e.gif", false)]
    [InlineData("f.png.exe", false)]
    [InlineData("noext", false)]
    public void Only_picture_files_load(string path, bool ok) => Assert.Equal(ok, ScreenShots.CanLoad(path));

    [Theory]
    [InlineData(1920, 1080, true)]
    [InlineData(7680, 4320, true)]
    [InlineData(0, 1080, false)]
    [InlineData(ScreenShots.MaxSide + 1, 100, false)]
    [InlineData(ScreenShots.MaxSide, ScreenShots.MaxSide, false)]   // a few-KB file can declare this: 1 GB decoded
    [InlineData(ScreenShots.MaxSide, 2160, true)]
    public void Oversized_or_empty_pictures_are_refused(int w, int h, bool ok) => Assert.Equal(ok, ScreenShots.SizeOk(w, h));

    [Fact]
    public void A_picture_of_the_monitors_shape_is_placed_over_the_monitor()
    {
        var monitor = new PixelRect(0, 0, 2560, 1440);
        Assert.Equal(monitor, ScreenShots.AreaFor(1920, 1080, monitor));
    }

    [Fact]
    public void A_picture_of_another_shape_is_a_backdrop_only() =>
        Assert.Null(ScreenShots.AreaFor(1000, 1000, new PixelRect(0, 0, 2560, 1440)));

    [Fact]
    public void Overlapping_rects_intersect_to_their_common_part() =>
        Assert.Equal(new PixelRect(100, 50, 1820, 1030), new PixelRect(100, 50, 2000, 2000).Intersect(new PixelRect(0, 0, 1920, 1080)));

    [Fact]
    public void Disjoint_rects_have_no_intersection() =>
        Assert.Null(new PixelRect(0, 0, 100, 100).Intersect(new PixelRect(200, 0, 100, 100)));
}
