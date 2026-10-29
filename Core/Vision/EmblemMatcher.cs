using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Compares two crops by their coarse grey structure: each is shrunk to a small grid of block means
/// and the grids are correlated. Used for the class emblem (PLAN §7 item 6) and for "is this still the skill
/// slot" checks.</summary>
public static class EmblemMatcher
{
    private const int Grid = 12;

    /// <summary>Similarity 0..1 (negative correlation clamps to 0). Sizes may differ; both are shrunk to the same grid.</summary>
    public static double Similarity(Image a, Image b)
    {
        Span<float> ta = stackalloc float[Grid * Grid], tb = stackalloc float[Grid * Grid];
        Thumb(a, ta);
        Thumb(b, tb);
        return Math.Max(0, ImageOps.Correlate(ta, tb));   // clamped to 0..1
    }

    /// <summary>Shrinks <paramref name="img"/> to a √(dst.Length)² grid of mean min(R,G,B) values.</summary>
    private static void Thumb(Image img, Span<float> dst)
    {
        int n = (int)Math.Sqrt(dst.Length);
        var px = img.Bgra;
        for (int gy = 0; gy < n; gy++)
        {
            int y0 = gy * img.Height / n, y1 = (gy + 1) * img.Height / n;
            for (int gx = 0; gx < n; gx++)
            {
                int x0 = gx * img.Width / n, x1 = (gx + 1) * img.Width / n;
                int sum = 0;
                for (int y = y0; y < y1; y++)
                    for (int i = y * img.Stride + x0 * 4, e = y * img.Stride + x1 * 4; i < e; i += 4)
                        sum += Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
                dst[gy * n + gx] = sum / (float)Math.Max(1, (x1 - x0) * (y1 - y0));
            }
        }
    }

}
