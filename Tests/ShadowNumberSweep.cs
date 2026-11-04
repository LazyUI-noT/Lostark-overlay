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
        int pad = int.Parse(Environment.GetEnvironmentVariable("GAUGE_SHADOW_PAD") ?? "0");
        var lines = new List<string>();
        foreach (var (t, img) in JeonbunSweep.Frames(video, rect.X, rect.Y - pad, rect.Width, rect.Height + 2 * pad, fps: 20))
            lines.Add($"{t:0.00} {BossDigits.ReadNumber(img, 1)?.ToString() ?? "-"}");
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }
}
