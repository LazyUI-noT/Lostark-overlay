namespace GaugeOverlay.Core.Domain;

/// <summary>A screenshot stretched over the screen area it shows (<c>area</c>, physical pixels) ↔ window spots in DIP
/// (physical = DIP × <c>dpiScale</c>). Only an image of the area's shape links; any other is a backdrop.</summary>
public static class ScreenMap
{
    /// <summary>How far the shapes may differ (a cropped or other-monitor image differs by far more).</summary>
    const double ShapeTolerance = 0.01;

    public static bool Linked(int imageW, int imageH, PixelRect area) =>
        imageW > 0 && imageH > 0 && area.Width > 0 && area.Height > 0
        && Math.Abs((double)imageW / imageH - (double)area.Width / area.Height) <= ShapeTolerance;

    public static (double X, double Y) DipToImage(double x, double y, double dpiScale, int imageW, int imageH, PixelRect area) =>
        ((x * dpiScale - area.X) * imageW / area.Width, (y * dpiScale - area.Y) * imageH / area.Height);

    public static (double X, double Y) ImageToDip(double x, double y, double dpiScale, int imageW, int imageH, PixelRect area) =>
        ((x * area.Width / imageW + area.X) / dpiScale, (y * area.Height / imageH + area.Y) / dpiScale);
}
