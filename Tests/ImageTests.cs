using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class ImageTests
{
    [Fact]
    public void Crop_copies_the_rect_and_clips_it_to_the_image()
    {
        var img = new Image(4, 3);
        for (int y = 0; y < 3; y++) for (int x = 0; x < 4; x++) img.Bgra[(y * 4 + x) * 4] = (byte)(10 * y + x);

        var c = img.Crop(new PixelRect(1, 1, 2, 2));
        Assert.Equal((2, 2), (c.Width, c.Height));
        Assert.Equal(11, c.Pixel(0, 0).B);
        Assert.Equal(22, c.Pixel(1, 1).B);

        var clipped = img.Crop(new PixelRect(3, 2, 5, 5));   // runs past the right and bottom
        Assert.Equal((1, 1), (clipped.Width, clipped.Height));
        Assert.Equal(23, clipped.Pixel(0, 0).B);

        var outside = img.Crop(new PixelRect(9, 9, 2, 2));
        Assert.Equal((0, 0), (outside.Width, outside.Height));
    }
}
