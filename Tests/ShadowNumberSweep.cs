using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>Dev only (GAUGE_SHADOW_VIDEO, FFMPEG): every frame of 대난투's number, as the engine reads it, to see which frames miss.</summary>
public class ShadowNumberSweep
{
    [Fact]
    public void Shadow_number_in_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_SHADOW_VIDEO") is not { } video) return;
        var rect = Geometry.RefShadowNumber;
        // GAUGE_SHADOW_PAD: rows added above and below the area ("4", or "8,0" for above and below apart).
        var pad = (Environment.GetEnvironmentVariable("GAUGE_SHADOW_PAD") ?? "0").Split(',').Select(int.Parse).ToArray();
        int up = pad[0], down = pad[^1];
        var lines = new List<string>();
        foreach (var (t, img) in JeonbunSweep.Frames(video, rect.X, rect.Y - up, rect.Width, rect.Height + up + down, fps: 20))
            lines.Add($"{t:0.00} {BossDigits.ReadNumber(img, 1)?.ToString() ?? "-"} {InkSpan(img)}");
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }

    /// <summary>The number's size: "h=<line> x0-x1 y0-y1" of the ink runs between blank columns (white, or red over blue as the
    /// reader's colour ink) at least 0.6 of the tallest, so the sweep shows the size the number took in each frame.</summary>
    static string InkSpan(Image img)
    {
        bool Ink(int x, int y)
        {
            var (b, g, r) = img.Pixel(x, y);
            return Math.Max(Math.Min(b, Math.Min(g, r)), r - b) > 150;
        }
        var runs = new List<(int X0, int X1, int Top, int Bottom)>();
        for (int x = 0, start = -1; x <= img.Width; x++)
        {
            bool ink = x < img.Width && Enumerable.Range(0, img.Height).Any(y => Ink(x, y));
            if (ink && start < 0) start = x;
            if (ink || start < 0) continue;
            var rows = Enumerable.Range(0, img.Height).Where(y => Enumerable.Range(start, x - start).Any(c => Ink(c, y))).ToList();
            runs.Add((start, x, rows[0], rows[^1] + 1));
            start = -1;
        }
        if (runs.Count == 0) return "-";
        int tallest = runs.Max(r => r.Bottom - r.Top);
        var tall = runs.Where(r => r.Bottom - r.Top >= 0.6 * tallest).ToList();
        var h = tall.Select(r => r.Bottom - r.Top).Order().ToList();
        return $"h={h[h.Count / 2]} {tall.Min(r => r.X0)}-{tall.Max(r => r.X1)} {tall.Min(r => r.Top)}-{tall.Max(r => r.Bottom)} n={tall.Count}";
    }
}
