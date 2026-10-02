using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Platform;

/// <summary>PNG files to and from <see cref="Image"/> (BGRA, alpha kept), for the gauge masks and screenshots.</summary>
public static class Png
{
    public static Image Load(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var img = new Image(bgra.PixelWidth, bgra.PixelHeight);
        bgra.CopyPixels(img.Bgra, img.Stride, 0);
        return img;
    }

    public static void Save(string path, Image img)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Bgra, img.Stride)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
