using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>ImageOps.Resample (every strip and icon brought to reference size): its weights are worked out once per column and
/// row (amsu-match, 2026-11-03). The picture must be the old one, byte for byte.</summary>
public class ResampleTests
{
    /// <summary>The old resample, per pixel.</summary>
    static Image Old(Image img, PixelRect r, double f)
    {
        int w = Math.Max(1, (int)Math.Round(r.Width * f)), h = Math.Max(1, (int)Math.Round(r.Height * f));
        var dst = new Image(w, h);
        for (int y = 0; y < h; y++)
        {
            double sy = Math.Clamp(r.Y + (y + 0.5) / f - 0.5, 0, img.Height - 1);
            int y0 = (int)sy, y1 = Math.Min(img.Height - 1, y0 + 1);
            double fy = sy - y0;
            for (int x = 0; x < w; x++)
            {
                double sx = Math.Clamp(r.X + (x + 0.5) / f - 0.5, 0, img.Width - 1);
                int x0 = (int)sx, x1 = Math.Min(img.Width - 1, x0 + 1);
                double fx = sx - x0;
                for (int c = 0; c < 4; c++)
                {
                    double t = img.Bgra[y0 * img.Stride + x0 * 4 + c] * (1 - fx) + img.Bgra[y0 * img.Stride + x1 * 4 + c] * fx;
                    double b = img.Bgra[y1 * img.Stride + x0 * 4 + c] * (1 - fx) + img.Bgra[y1 * img.Stride + x1 * 4 + c] * fx;
                    dst.Bgra[y * dst.Stride + x * 4 + c] = (byte)Math.Round(t * (1 - fy) + b * fy);
                }
            }
        }
        return dst;
    }

    [Theory]
    [InlineData(0.908)]   // the 110% boss row at 1080p
    [InlineData(0.816)]
    [InlineData(1.226)]
    [InlineData(0.5)]
    [InlineData(2.0)]
    [InlineData(1.0 / 1.333)]
    public void The_picture_is_the_old_one_byte_for_byte(double f)
    {
        var rng = new Random(7);
        var img = new Image(97, 41, [.. Enumerable.Range(0, 97 * 41 * 4).Select(_ => (byte)rng.Next(256))]);
        foreach (var r in new[] { new PixelRect(0, 0, 97, 41), new PixelRect(5, 3, 60, 30), new PixelRect(90, 35, 7, 6) })
        {
            var (old, now) = (Old(img, r, f), ImageOps.Resample(img, r, f));
            Assert.Equal((old.Width, old.Height), (now.Width, now.Height));
            Assert.Equal(old.Bgra, now.Bgra);
        }
    }

    [Fact]
    public void A_real_strip_is_the_old_one_byte_for_byte()
    {
        var frame = Fixture.Load("bitam_110").Frames[0];
        var all = new PixelRect(0, 0, frame.Width, frame.Height);
        Assert.Equal(Old(frame, all, 1 / 1.1).Bgra, ImageOps.Resample(frame, all, 1 / 1.1).Bgra);
    }
}
