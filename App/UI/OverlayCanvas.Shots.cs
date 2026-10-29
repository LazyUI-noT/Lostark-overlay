using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The picture under the overlays: a profile's screenshot (<see cref="ScreenShots"/>), one waiting for 덮어쓰기, or
/// the sample screen. A picture of the screen's shape lies where it was on screen; any other is only a backdrop.</summary>
public sealed partial class OverlayCanvas
{
    static readonly Uri SampleScreen = new("pack://application:,,,/Assets/sample-screen.jpg");

    /// <summary>The sample screen, over the whole screen.</summary>
    public void ShowSampleScreen() => ShowPicture(new BitmapImage(SampleScreen), null);

    /// <summary>Over the screen area it shows (physical pixels → DIP), or over the whole screen.</summary>
    public void ShowPicture(BitmapSource picture, PixelRect? area)
    {
        _shot.Source = picture;
        var dpi = VisualTreeHelper.GetDpi(this);
        var r = area is { } a ? new Rect(a.X / dpi.DpiScaleX - _screen.Left, a.Y / dpi.DpiScaleY - _screen.Top, a.Width / dpi.DpiScaleX, a.Height / dpi.DpiScaleY)
            : new Rect(0, 0, _screen.Width, _screen.Height);
        (_shot.Width, _shot.Height) = (r.Width, r.Height);
        Canvas.SetLeft(_shot, r.X);
        Canvas.SetTop(_shot, r.Y);
    }

    /// <summary>The picture in <paramref name="uri"/>, read at once (the file isn't locked, so 덮어쓰기 can replace it);
    /// null when it can't be read or is too big (checked before the pixels are decoded).</summary>
    public static BitmapSource? Decode(Uri uri)
    {
        try
        {
            var frame = BitmapDecoder.Create(uri, BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (!ScreenShots.SizeOk(frame.PixelWidth, frame.PixelHeight)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            (image.UriSource, image.CacheOption, image.CreateOptions) = (uri, BitmapCacheOption.OnLoad, BitmapCreateOptions.IgnoreImageCache);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // WIC also throws COMException, OverflowException, … on a damaged file
        {
            return null;
        }
    }
}
