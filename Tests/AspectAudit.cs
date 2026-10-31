using System.Diagnostics;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Xunit.Abstractions;

namespace GaugeOverlay.Tests;

/// <summary>Dev audit (no screen, runs only when asked): every reader that places areas on the game area, run on frames of a video
/// of another size or aspect (a friend's 21:9 2560×1080 game), with the fit the app's own solver finds there. Set GAUGE_AUDIT (the
/// video), GAUGE_AUDIT_VP ("x,y,w,h": the game area in the video), GAUGE_AUDIT_AT (seconds, "10,30"), FFMPEG, and optionally
/// GAUGE_AUDIT_OUT (a folder: each frame's areas saved as .bgra with their size in the name, for a look).</summary>
public class AspectAudit(ITestOutputHelper o)
{
    [Fact]
    public void Audit_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_AUDIT") is not { } video) return;
        int[] a = Environment.GetEnvironmentVariable("GAUGE_AUDIT_VP")!.Split(',').Select(int.Parse).ToArray();
        // GAUGE_AUDIT_SCALE: the frame shrunk first (0.75: a 1440p video played on a 1080p screen, as a replay shows it).
        double shrink = double.TryParse(Environment.GetEnvironmentVariable("GAUGE_AUDIT_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out var sv) ? sv : 1;
        string? outDir = Environment.GetEnvironmentVariable("GAUGE_AUDIT_OUT");
        foreach (double t in Environment.GetEnvironmentVariable("GAUGE_AUDIT_AT")!.Split(',').Select(double.Parse))
        {
            var img = Frame(video, t, a[0], a[1], a[2], a[3], shrink);
            var vp = new Viewport(0, 0, img.Width, img.Height);
            o.WriteLine($"── {t} s, game area {vp.Width}×{vp.Height} ({vp.Width / (double)vp.Height:0.00}:1)");
            var band = FitSolver.Band(vp);
            var fit = FitSolver.Solve(img.Crop(band), band, vp, video: true);
            if (fit is null) { o.WriteLine("fit: none (skill bar not found)"); continue; }
            double scale = Geometry.Scale(vp, fit);
            o.WriteLine($"fit: HUD {fit.Hud:0.#}, nudge ({fit.NudgeX:0.#}, {fit.NudgeY:0.#}), bar {Geometry.BarRect(vp, fit)}, score {FitSolver.Score(img.Crop(band), band, vp, fit):0.00}");

            var row = Geometry.BossTextRow(vp, fit);
            var bar = BossDigits.ReadBar(img.Crop(row), scale);
            o.WriteLine($"boss text row {row}: count {bar.Count?.ToString() ?? "-"}, hp {bar.Hp?.ToString() ?? "-"}");
            var bossRow = Geometry.BossRow(vp, fit);
            o.WriteLine($"boss effect row {bossRow}: counts {DebuffReader.BossCounts(img.Crop(bossRow), Geometry.BossScale(vp, fit))}");

            var sizeArea = BuffSizeFinder.Area(vp, fit);
            var size = BuffSizeFinder.Find(img.Crop(sizeArea), sizeArea, vp, fit);
            int buffSize = size?.Size ?? 120;
            o.WriteLine($"buff size: {size?.ToString() ?? "not found (120 assumed)"}");
            var buffArea = Geometry.BuffArea(vp, fit, buffSize);
            var buffImg = img.Crop(buffArea);
            var timers = new BuffReader(Geometry.BuffScale(vp, buffSize));
            var buffs = Geometry.BuffBoxes(vp, fit, buffSize).Select(b => BuffReader.Classify(buffImg, Local(b.Icon, buffArea)) is { } k
                ? $"{k}({timers.ReadTimer(buffImg, Local(b.Timer, buffArea))?.ToString("0.#") ?? "?"}s)" : "·");
            o.WriteLine($"buff row {buffArea}: {string.Join(" ", buffs)}");
            var debuffArea = Geometry.DebuffArea(vp, fit, buffSize);
            var debuffImg = img.Crop(debuffArea);
            var debuffs = Geometry.DebuffBoxes(vp, fit, buffSize).Select(b => Local(b.Icon, debuffArea)).Select(i =>
                DebuffReader.Filled(debuffImg, i) ? DebuffReader.Classify(debuffImg, i)?.ToString() ?? "?" : "·");
            o.WriteLine($"debuff row {debuffArea}: {string.Join(" ", debuffs)}");
            o.WriteLine($"단심 band {Geometry.JudgmentBand(vp)}");
            o.WriteLine($"전분 panel: {JeonbunFinder.Find(img, vp.Height / Geometry.ReferenceHeight)?.ToString() ?? "not found"}");

            if (outDir is null) continue;
            Directory.CreateDirectory(outDir);
            foreach (var (name, r) in new[] { ("bossrow", row), ("bosseffects", bossRow), ("buffs", buffArea), ("debuffs", debuffArea), ("bar", Geometry.BarRect(vp, fit)) })
            {
                var c = img.Crop(r);
                File.WriteAllBytes(Path.Combine(outDir, $"{t:0}s_{name}_{c.Width}x{c.Height}.bgra"), c.Bgra);
            }
        }
    }

    static PixelRect Local(PixelRect r, PixelRect area) => r with { X = r.X - area.X, Y = r.Y - area.Y };

    /// <summary>One frame at <paramref name="t"/> s, cropped (FFMPEG decodes it).</summary>
    static Image Frame(string video, double t, int x, int y, int w, int h, double shrink)
    {
        string filter = $"format=bgra,crop={w}:{h}:{x}:{y}";
        (w, h) = ((int)Math.Round(w * shrink), (int)Math.Round(h * shrink));
        if (shrink != 1) filter += $",scale={w}:{h}:flags=bilinear";
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("FFMPEG")!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "-v", "error", "-ss", t.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", video, "-frames:v", "1",
            "-vf", filter, "-f", "rawvideo", "-pix_fmt", "bgra", "-" })
            psi.ArgumentList.Add(arg);
        using var ff = Process.Start(psi)!;
        _ = ff.StandardError.ReadToEndAsync();
        var buf = new byte[w * h * 4];
        int got = 0;
        while (got < buf.Length && ff.StandardOutput.BaseStream.Read(buf, got, buf.Length - got) is var n and > 0) got += n;
        ff.WaitForExit();
        return new Image(w, h, buf);
    }
}
