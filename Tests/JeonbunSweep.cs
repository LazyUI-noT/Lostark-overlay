using System.Diagnostics;
using System.Text;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Dev check, off by default: runs JeonbunReader over every half second of a video's 전분 panel and writes a CSV
/// (video second, the reading, the raw timer and cell texts). Set GAUGE_SWEEP to the video, GAUGE_SWEEP_OUT to the CSV, FFMPEG
/// to ffmpeg.exe, and optionally GAUGE_SWEEP_RECT ("x,y,w,h", default the 1080p spot 1448,849,470,88).</summary>
public sealed class JeonbunSweep
{
    [Fact]
    public void Sweep_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_SWEEP") is not { } video) return;
        var rect = (Environment.GetEnvironmentVariable("GAUGE_SWEEP_RECT") ?? "1448,849,470,88").Split(',').Select(int.Parse).ToArray();
        var csv = new StringBuilder("t,timer,damage,dps,support,timertext,cells\n");
        foreach (var (t, img) in Frames(video, rect[0], rect[1], rect[2], rect[3]))
        {
            var r = JeonbunReader.Read(img);
            csv.Append($"{t:0.0},{r?.Timer},{r?.Damage},{r?.Dps},{r?.Support},{JeonbunReader.TimerText(img)},{string.Join("|", JeonbunReader.Cells(img))}\n");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, csv.ToString());
    }

    /// <summary>GAUGE_FIND: JeonbunFinder on the whole 1920×1080 frame every 5 s; the spot found (or nothing) per line in GAUGE_SWEEP_OUT.</summary>
    [Fact]
    public void Find_in_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_FIND") is not { } video) return;
        var lines = new List<string>();
        int i = 0;
        foreach (var (t, img) in Frames(video, 0, 0, 1920, 1080))
            if (i++ % 10 == 0)
            {
                var sw = Stopwatch.StartNew();
                var r = GaugeOverlay.Core.Vision.JeonbunFinder.Find(img, 1.0);
                lines.Add($"{t:0} {r?.ToString() ?? "-"} {sw.ElapsedMilliseconds} ms");
            }
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }

    /// <summary>GAUGE_COUNT: the boss bar count ("X n") and HP text over every half second of a 1920×1080 video, for each HUD
    /// scale in GAUGE_COUNT_K (default "0.818,0.909,1": HUD 90/100/110); one line per second in GAUGE_SWEEP_OUT.</summary>
    [Fact]
    public void Count_in_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_COUNT") is not { } video) return;
        var ks = (Environment.GetEnvironmentVariable("GAUGE_COUNT_K") ?? "0.818,0.909,1").Split(',').Select(double.Parse).ToArray();
        var lines = new List<string>();
        foreach (var (t, img) in Frames(video, 0, 0, 1920, 140))
        {
            var vp = new Viewport(0, 0, 1920, 1080);
            var cells = ks.Select(k =>
            {
                var fit = new Fit(Geometry.ReferenceHud * k);
                var c = ImageOps.Crop(img, Geometry.BossCount(vp, fit));
                var hp = BossDigits.ReadHp(ImageOps.Crop(img, Geometry.BossHpText(vp, fit)), k);
                return $"{BossDigits.ReadCount(c, k)?.ToString() ?? "-"} {hp?.Current.ToString() ?? "-"}";
            });
            lines.Add($"{t:0.0} | {string.Join(" | ", cells)}");
        }
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }

    /// <summary>The rectangle of every half second of the video (FFMPEG decodes it), with its video second.</summary>
    internal static IEnumerable<(double T, Image Image)> Frames(string video, int x, int y, int w, int h)
    {
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("FFMPEG")!)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "-v", "error", "-i", video, "-vf", $"fps=2,crop={w}:{h}:{x}:{y},format=bgra", "-f", "rawvideo", "-pix_fmt", "bgra", "-" })
            psi.ArgumentList.Add(a);
        using var ff = Process.Start(psi)!;
        _ = ff.StandardError.ReadToEndAsync();
        var stream = ff.StandardOutput.BaseStream;
        for (int i = 0; ; i++)
        {
            var buf = new byte[w * h * 4];
            int got = 0;
            while (got < buf.Length && stream.Read(buf, got, buf.Length - got) is var n and > 0) got += n;
            if (got < buf.Length) break;
            yield return (i / 2.0, new Image(w, h, buf));
        }
        ff.WaitForExit();
    }
}
