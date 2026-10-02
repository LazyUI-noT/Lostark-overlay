using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>오버레이 프로필 screenshots: one PNG per profile (<c>screens\1.png</c> … <c>5.png</c> beside settings.json),
/// taken of the game or loaded from a picture file.</summary>
public static class ScreenShots
{
    /// <summary>Longer than any monitor's side: a bigger picture is refused rather than decoded (8K is 7680).</summary>
    public const int MaxSide = 16384;

    /// <summary>Twice 8K's pixels: a small file can declare 16384×16384, a gigabyte once decoded.</summary>
    const long MaxPixels = 2L * 7680 * 4320;

    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GaugeOverlay", "screens");

    static readonly string[] Loadable = [".png", ".jpg", ".jpeg", ".bmp"];

    public static string PathOf(string dir, int profile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(profile);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(profile, OverlayProfile.Count);
        return Path.Combine(dir, $"{profile + 1}.png");
    }

    /// <summary>A picture file 불러오기 accepts (by its extension).</summary>
    public static bool CanLoad(string path) => Loadable.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool SizeOk(int width, int height) =>
        width is > 0 and <= MaxSide && height is > 0 and <= MaxSide && (long)width * height <= MaxPixels;

    /// <summary>Where a loaded picture lies on screen: over the whole monitor when it has the monitor's shape (a
    /// screenshot of it), else nowhere (null: a backdrop, window spots only relative).</summary>
    public static PixelRect? AreaFor(int width, int height, PixelRect monitor) =>
        ScreenMap.Linked(width, height, monitor) ? monitor : null;
}
