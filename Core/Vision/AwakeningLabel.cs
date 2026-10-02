using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The 각성기 변화 banner above 각성기 (PLAN §3 "Other"): a long hexagon "✳ key" that appears once the 각성기
/// gauge is full — red while 각성기 is ready, dark grey while it is still on cooldown. Both count. The key text follows
/// the user's binding and the colours change, but the ✳ always sits at the same spot with the same shape, lighter than
/// the body around it, so a few pixels of it decide.</summary>
public static class AwakeningLabel
{
    // The ✳'s centre in Geometry.RefAwakeningLabel at reference scale (gameplay.mp4 puts it at x 21, the screenshots at
    // 23–25), points on its strokes and in the body between them (reference px from the centre), and the search
    // around the fitted spot that covers that spread.
    private const int CentreX = 22, CentreY = 11, Search = 4;
    private static readonly (int X, int Y)[] Strokes = [(0, -3), (0, 0), (0, 3), (-3, 0), (3, 0)];
    private static readonly (int X, int Y)[] Body = [(-2, -4), (2, -4), (-2, 4), (2, 4), (-7, 0), (7, 0)];
    // Darkest stroke minus lightest body point, in luminance. gameplay.mp4 and both screenshots: 47–130 with the banner
    // (grey 47–72, red 89–130, fading in included); 각성기 변화_sample.mp4, every frame over dark and bright backgrounds:
    // grey 46–53, red 69–77, apart from the ready flash (see Hold); every frame without it in the four videos: ≤ 7.
    private const double MinContrast = 25;

    /// <summary>True if <paramref name="labelCrop"/> (Geometry.RefAwakeningLabel mapped to the screen) shows the banner.</summary>
    public static bool IsShown(Image labelCrop)
    {
        double k = labelCrop.Width / (double)Geometry.RefAwakeningLabel.Width;
        for (int dy = -Search; dy <= Search; dy++)
            for (int dx = -Search; dx <= Search; dx++)
            {
                double cx = CentreX * k + dx, cy = CentreY * k + dy, darkestStroke = double.MaxValue, lightestBody = 0;
                foreach (var (x, y) in Strokes) darkestStroke = Math.Min(darkestStroke, Lum(labelCrop, cx + x * k, cy + y * k));
                foreach (var (x, y) in Body) lightestBody = Math.Max(lightestBody, Lum(labelCrop, cx + x * k, cy + y * k));
                if (darkestStroke - lightestBody >= MinContrast) return true;
            }
        return false;
    }

    /// <summary>The banner state after a read: the white flash as 각성기 comes off cooldown washes the banner out for
    /// ~0.2 s (각성기 변화_sample.mp4 3.2 s), which one read can land in, so it turns off only when two reads in a row
    /// miss it.</summary>
    public static bool Hold(bool seen, bool wasOn, bool missedLast) => seen || (wasOn && !missedLast);

    private static double Lum(Image img, double x, double y)
    {
        int i = Math.Clamp((int)Math.Round(y), 0, img.Height - 1) * img.Stride + Math.Clamp((int)Math.Round(x), 0, img.Width - 1) * 4;
        return 0.114 * img.Bgra[i] + 0.587 * img.Bgra[i + 1] + 0.299 * img.Bgra[i + 2];
    }
}
