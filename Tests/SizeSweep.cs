using System.Globalization;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Dev check, off by default (video-settings.plan.md): the in-game sizes a video was recorded at, read off it
/// every 2 s. Set GAUGE_SIZES to the video, GAUGE_SWEEP_OUT to the output and FFMPEG to ffmpeg.exe. One line per sample: the
/// second, the HUD (FitSolver, the HP bar), the buffs each 버프 크기's 14 boxes hold. 보스 디버프 크기 is
/// not read: the 암수 walk's spacing at a guessed size gave 60–113 on 빛암 80_90.</summary>
public sealed class SizeSweep
{
    // GAUGE_SIZES_VP "x,y,w,h": the game area in the video (a 21:9 game letterboxed in 2560×1440: "0,180,2560,1080"), cut out
    // so it is read as a whole frame; GAUGE_SIZES_BOXES: how many buff boxes each size tries (default 14).
    static readonly int[] Area = Environment.GetEnvironmentVariable("GAUGE_SIZES_VP")?.Split(',').Select(int.Parse).ToArray() ?? [0, 0, 1920, 1080];
    static readonly Viewport Vp = new(0, 0, Area[2], Area[3]);
    static readonly int Boxes = int.TryParse(Environment.GetEnvironmentVariable("GAUGE_SIZES_BOXES"), out int n) ? n : Geometry.MaxBuffBoxCount;

    [Fact]
    public void Sizes_of_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_SIZES") is not { } video) return;
        var lines = new List<string> { "t hud buff[80,90,100,110,120]" };
        foreach (var (t, img) in JeonbunSweep.Frames(video, Area[0], Area[1], Vp.Width, Vp.Height, fps: 1))
            if ((int)t % 2 == 0)
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{t:0} {Line(img)}"));
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }

    static string Line(Image frame)
    {
        var band = FitSolver.Band(Vp);
        if (FitSolver.Solve(frame.Crop(band), band, Vp, video: true) is not { } fit) return "-";
        var buffs = Settings.BuffSizes.Select(size => Geometry.BuffBoxes(Vp, fit, size, Boxes)
            .Count(b => Inside(frame, b.Icon) && BuffReader.Classify(frame, b.Icon) is not null));
        return $"{fit.Hud:0} [{string.Join(",", buffs)}]";
    }

    static bool Inside(Image img, PixelRect r) => r.X >= 0 && r.Y >= 0 && r.Right <= img.Width && r.Bottom <= img.Height;
}
