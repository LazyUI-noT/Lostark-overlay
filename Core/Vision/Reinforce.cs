using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>테스트 모드 강화 읽기 (reinforced-reader.plan.md): a capture of compressed video (a stream, a 1440p game in 1080p, 720p)
/// cleaned before the normal reader reads it, so the game's own path never changes. Pure.</summary>
public static class Reinforce
{
    /// <summary>How much of the detail a 3×3 blur takes away is added back (unsharp mask): the digits' edges, smeared by scaling
    /// and compression, sharpen again. Buff sweep (tracked timers within 1 s, buff_gameplay.mp4 at 5 fps): 720p 36 → 95%, 4 Mbps
    /// 99 → 100%; 0.7 and 1.0 helped 720p less (55, 91%). A 3×3 median first, and voting between amounts, did worse.</summary>
    const double Amount = 1.5;

    /// <summary>The capture's levels stretched: its darkest 1% to black and brightest 0.5% to white (one mapping for all three
    /// channels, so colours keep their hue). Scaling dims thin white text under the readers' ink thresholds; this brings it back.</summary>
    public static Image Levels(Image img)
    {
        var src = img.Bgra;
        var hist = new int[256];
        for (int i = 0; i < src.Length; i += 4) hist[Math.Max(src[i], Math.Max(src[i + 1], src[i + 2]))]++;
        int n = src.Length / 4, lo = 0, hi = 255;
        for (int acc = 0; lo < 255 && (acc += hist[lo]) < n * 0.01; lo++) { }
        for (int acc = 0; hi > 0 && (acc += hist[hi]) < n * 0.005; hi--) { }
        if (hi - lo < 32) return img;   // flat: nothing to stretch
        var dst = (byte[])src.Clone();
        for (int i = 0; i < src.Length; i += 4)
            for (int c = 0; c < 3; c++)
                dst[i + c] = (byte)Math.Clamp((src[i + c] - lo) * 255 / (hi - lo), 0, 255);
        return new Image(img.Width, img.Height, dst);
    }

    /// <summary>The capture as the readers should see it: sharpened, each colour channel; alpha and the 1 px border kept.</summary>
    public static Image Clean(Image img)
    {
        int w = img.Width, h = img.Height, stride = img.Stride;
        var src = img.Bgra;
        var dst = (byte[])src.Clone();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * stride + x * 4;
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            sum += src[i + dy * stride + dx * 4 + c];
                    dst[i + c] = (byte)Math.Clamp(Math.Round(src[i + c] + Amount * (src[i + c] - sum / 9.0)), 0, 255);
                }
            }
        return new Image(w, h, dst);
    }
}
