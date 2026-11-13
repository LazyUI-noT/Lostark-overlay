using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Pixel helpers the readers share: crop, bilinear resample (BGRA and grey), luminance and correlation. Each
/// keeps the exact arithmetic its readers were tuned on; readers whose maths differs on purpose (a masked or shifted
/// correlation, luminance / 256.0, a float-mean correlation) keep their own.</summary>
public static class ImageOps
{
    public static Image Crop(Image img, PixelRect r)
    {
        var dst = new Image(r.Width, r.Height);
        for (int y = 0; y < r.Height; y++) img.Row(r.Y + y).Slice(r.X * 4, r.Width * 4).CopyTo(dst.Bgra.AsSpan(y * dst.Stride));
        return dst;
    }

    /// <summary><paramref name="r"/> of <paramref name="img"/> scaled by <paramref name="f"/> (bilinear; pixels past the
    /// edge repeat it). Written into <paramref name="into"/> when it has the result's size (every byte is written), else a new image.</summary>
    public static Image Resample(Image img, PixelRect r, double f, Image? into = null)
    {
        int w = Math.Max(1, (int)Math.Round(r.Width * f)), h = Math.Max(1, (int)Math.Round(r.Height * f));
        var dst = into is not null && into.Width == w && into.Height == h ? into : new Image(w, h);
        // Each column's source pixels and weight once (the same values the per-pixel sum used: the picture is unchanged).
        var x0s = new int[w];
        var x1s = new int[w];
        var fxs = new double[w];
        for (int x = 0; x < w; x++)
        {
            double sx = Math.Clamp(r.X + (x + 0.5) / f - 0.5, 0, img.Width - 1);
            x0s[x] = (int)sx;
            x1s[x] = Math.Min(img.Width - 1, x0s[x] + 1);
            fxs[x] = sx - x0s[x];
        }
        byte[] src = img.Bgra, to = dst.Bgra;
        for (int y = 0; y < h; y++)
        {
            double sy = Math.Clamp(r.Y + (y + 0.5) / f - 0.5, 0, img.Height - 1);
            int y0 = (int)sy, y1 = Math.Min(img.Height - 1, y0 + 1);
            double fy = sy - y0, gy = 1 - fy;
            int top = y0 * img.Stride, bottom = y1 * img.Stride, o = y * dst.Stride;
            for (int x = 0; x < w; x++, o += 4)
            {
                int a = x0s[x] * 4, b = x1s[x] * 4;
                double fx = fxs[x], gx = 1 - fx;
                to[o] = Mix(src, top + a, top + b, bottom + a, bottom + b, gx, fx, gy, fy);
                to[o + 1] = Mix(src, top + a + 1, top + b + 1, bottom + a + 1, bottom + b + 1, gx, fx, gy, fy);
                to[o + 2] = Mix(src, top + a + 2, top + b + 2, bottom + a + 2, bottom + b + 2, gx, fx, gy, fy);
                to[o + 3] = Mix(src, top + a + 3, top + b + 3, bottom + a + 3, bottom + b + 3, gx, fx, gy, fy);
            }
        }
        return dst;
    }

    /// <summary>One channel of a bilinear pixel: the top pair, the bottom pair, then the two rows.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    static byte Mix(byte[] s, int tl, int tr, int bl, int br, double gx, double fx, double gy, double fy) =>
        (byte)Math.Round((s[tl] * gx + s[tr] * fx) * gy + (s[bl] * gx + s[br] * fx) * fy);

    /// <summary>Bilinear resample of a <paramref name="sw"/>×<paramref name="sh"/> grey image to <paramref name="w"/>×<paramref name="h"/>.</summary>
    public static float[] Resample(float[] src, int sw, int sh, int w, int h)
    {
        var dst = new float[w * h];
        double kx = (double)sw / w, ky = (double)sh / h;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double fx = Math.Clamp((x + 0.5) * kx - 0.5, 0, sw - 1), fy = Math.Clamp((y + 0.5) * ky - 0.5, 0, sh - 1);
                int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
                double tx = fx - x0, ty = fy - y0;
                float L(int px, int py) => src[py * sw + px];
                dst[y * w + x] = (float)((L(x0, y0) * (1 - tx) + L(x1, y0) * tx) * (1 - ty) + (L(x0, y1) * (1 - tx) + L(x1, y1) * tx) * ty);
            }
        return dst;
    }

    /// <summary>Fast luminance (29·B + 150·G + 77·R) >> 8, truncated, of <paramref name="r"/> in <paramref name="img"/>, row by row.</summary>
    public static float[] LumaFast(Image img, PixelRect r)
    {
        var v = new float[r.Width * r.Height];
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                int i = (r.Y + y) * img.Stride + (r.X + x) * 4;
                v[y * r.Width + x] = (img.Bgra[i] * 29 + img.Bgra[i + 1] * 150 + img.Bgra[i + 2] * 77) >> 8;
            }
        return v;
    }

    /// <summary>Perceptual luminance (0.114·B + 0.587·G + 0.299·R) of the pixel at byte <paramref name="p"/>.</summary>
    public static float LumaPerceptual(byte[] bgra, int p) => 0.114f * bgra[p] + 0.587f * bgra[p + 1] + 0.299f * bgra[p + 2];

    /// <summary>Plain correlation of two equal-length vectors, −1..1.</summary>
    public static double Correlate(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double ma = 0, mb = 0;
        for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
        ma /= a.Length; mb /= b.Length;
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        return saa < 1e-6 || sbb < 1e-6 ? 0 : sab / Math.Sqrt(saa * sbb);
    }
}
