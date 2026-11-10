using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>BuffReader.Match without crops (perf-hotspots.plan.md step 3a) gives bit for bit what the crop-per-offset code gave:
/// the buff, debuff and 암수 reads all go through it. The reference below is that code as it was (2026-11-08).</summary>
public class BuffMatchExactTests
{
    static readonly string[] Rows = ["buffrow_bg", "buffrow_down", "buffrow_hud80", "buffrow_hud90"];

    public static TheoryData<string> Fixtures() => [.. Rows];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Thumbs_match_the_crop_code_bit_for_bit(string fixture)
    {
        foreach (var frame in Fixture.Load(fixture).Frames.Take(12))
            foreach (var r in Icons(frame))
                Assert.Equal(RefThumb(ImageOps.Crop(frame, r)), BuffReader.Thumb(ImageOps.Crop(frame, r)));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Scores_match_the_crop_code_bit_for_bit(string fixture)
    {
        var frames = Fixture.Load(fixture).Frames;
        // Templates and look-alikes from the fixture's own first icons: the algorithm is under test, not the template set.
        var first = Icons(frames[0]).Take(4).Select(r => ImageOps.Crop(frames[0], BuffReader.Interior(r))).ToArray();
        float[][] thumbs = [.. first.Select(RefThumb)];
        double[] hues = [.. first.Select(t => SlotClassifier.ColourStats(t).MeanHue)];
        (int, float[])[] lookAlikes = [(0, thumbs[^1]), (1, thumbs[0])];
        foreach (var frame in frames.Take(12))
            foreach (var r in Icons(frame))
            {
                var want = RefMatch(frame, r, thumbs, hues, lookAlikes);
                var got = BuffReader.Match(frame, r, thumbs, hues, lookAlikes);
                Assert.Equal(want.Structure, got.Structure);
                Assert.Equal(want.Hue, got.Hue);
                Assert.Equal(want.LookAlike, got.LookAlike);
            }
    }

    /// <summary>Icon-sized squares over the whole frame, some at its edges (the search clips there).</summary>
    static IEnumerable<PixelRect> Icons(Image frame)
    {
        int size = Math.Min(frame.Height, 31);
        for (int x = 0; x + size <= frame.Width; x += 13)
            for (int y = 0; y + size <= frame.Height; y += Math.Max(1, (frame.Height - size) / 2))
                yield return new PixelRect(x, y, size, size);
    }

    // ── The reference: BuffReader's Match, Thumb and ChannelNcc before step 3a, unchanged ─────────────────────────────
    const int Grid = 10;

    static (double[] Structure, double[] Hue, double[] LookAlike) RefMatch(Image area, PixelRect icon, float[][] thumbs, double[] hues,
        (int Of, float[] Thumb)[] lookAlikes)
    {
        var inner = BuffReader.Interior(icon);
        int search = Math.Max(1, inner.Width / 12);
        var best = Enumerable.Repeat(-2.0, thumbs.Length).ToArray();
        var hue = new double[thumbs.Length];
        var lookAlike = Enumerable.Repeat(-2.0, thumbs.Length).ToArray();
        for (int dy = -search; dy <= search; dy++)
            for (int dx = -search; dx <= search; dx++)
            {
                var r = inner with { X = inner.X + dx, Y = inner.Y + dy };
                if (r.X < 0 || r.Y < 0 || r.Right > area.Width || r.Bottom > area.Height) continue;
                var crop = ImageOps.Crop(area, r);
                var thumb = RefThumb(crop);
                foreach (var (of, t) in lookAlikes) lookAlike[of] = Math.Max(lookAlike[of], RefNcc(thumb, t));
                for (int k = 0; k < thumbs.Length; k++)
                {
                    double s = RefNcc(thumb, thumbs[k]);
                    if (s <= best[k]) continue;
                    best[k] = s;
                    hue[k] = BuffReader.HueDistance(SlotClassifier.ColourStats(crop).MeanHue, hues[k]);
                }
            }
        return (best, hue, lookAlike);
    }

    static float[] RefThumb(Image img)
    {
        var t = new float[Grid * Grid * 3];
        for (int gy = 0; gy < Grid; gy++)
        {
            int y0 = gy * img.Height / Grid, y1 = Math.Max(y0 + 1, (gy + 1) * img.Height / Grid);
            for (int gx = 0; gx < Grid; gx++)
            {
                int x0 = gx * img.Width / Grid, x1 = Math.Max(x0 + 1, (gx + 1) * img.Width / Grid), b = 0, g = 0, r = 0;
                for (int y = y0; y < y1; y++)
                    for (int i = y * img.Stride + x0 * 4, e = y * img.Stride + x1 * 4; i < e; i += 4)
                    { b += img.Bgra[i]; g += img.Bgra[i + 1]; r += img.Bgra[i + 2]; }
                float n = (x1 - x0) * (y1 - y0);
                int c = (gy * Grid + gx) * 3;
                (t[c], t[c + 1], t[c + 2]) = (b / n, g / n, r / n);
            }
        }
        return t;
    }

    static double RefNcc(float[] a, float[] b)
    {
        Span<double> ma = stackalloc double[3], mb = stackalloc double[3];
        for (int i = 0; i < a.Length; i++) { ma[i % 3] += a[i]; mb[i % 3] += b[i]; }
        for (int c = 0; c < 3; c++) { ma[c] /= a.Length / 3; mb[c] /= b.Length / 3; }
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { double da = a[i] - ma[i % 3], db = b[i] - mb[i % 3]; sab += da * db; saa += da * da; sbb += db * db; }
        return saa < 1e-6 || sbb < 1e-6 ? 0 : sab / Math.Sqrt(saa * sbb);
    }
}
