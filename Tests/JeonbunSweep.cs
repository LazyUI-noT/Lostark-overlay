using System.Diagnostics;
using System.Text;
using GaugeOverlay.Core.Cooldown;
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
        // GAUGE_COUNT_HUD: HUD % values instead (e.g. "75,80,85,90"); GAUGE_COUNT_VP: the game area in the video "x,y,w,h"
        // (a 21:9 game letterboxed in a 2560×1440 video: "0,180,2560,1080").
        var ks = Environment.GetEnvironmentVariable("GAUGE_COUNT_HUD") is { } huds
            ? huds.Split(',').Select(h => double.Parse(h) / Geometry.ReferenceHud).ToArray()
            : (Environment.GetEnvironmentVariable("GAUGE_COUNT_K") ?? "0.818,0.909,1").Split(',').Select(double.Parse).ToArray();
        int[] area = Environment.GetEnvironmentVariable("GAUGE_COUNT_VP")?.Split(',').Select(int.Parse).ToArray() ?? [0, 0, 1920, 1080];
        int band = (int)Math.Ceiling(140 * area[3] / Geometry.ReferenceHeight);   // the bar's rows, from the area's top
        var lines = new List<string>();
        foreach (var (t, img) in Frames(video, area[0], area[1], area[2], band))
        {
            var vp = new Viewport(0, 0, area[2], area[3]);
            var cells = ks.Select(k =>
            {
                var fit = new Fit(Geometry.ReferenceHud * k);
                double scale = Geometry.Scale(vp, fit);   // k only while the area is 1080 tall
                var c = ImageOps.Crop(img, Geometry.BossCount(vp, fit));
                var hp = BossDigits.ReadHp(ImageOps.Crop(img, Geometry.BossHpText(vp, fit)), scale);
                return $"{BossDigits.ReadCount(c, scale)?.ToString() ?? "-"} {hp?.Current.ToString() ?? "-"}/{hp?.Max.ToString() ?? "-"}";
            });
            lines.Add($"{t:0.0} | {string.Join(" | ", cells)}");
        }
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }

    /// <summary>GAUGE_AMSU: the 암수 loop as the engine runs it (<see cref="AmsuWatch"/>: its read times, row lock, boss HP from the
    /// bar's text) over a video at 10 frames a second, the cadence's fastest pass. GAUGE_AMSU_SIZE: the 보스 디버프 크기 (default 100);
    /// GAUGE_AMSU_HUD (default 100); GAUGE_AMSU_VP: the game in the video "x,y,w,h" (default 0,0,1920,1080; the 21:9 clip
    /// letterboxed in 2560×1440: 0,180,2560,1080). GAUGE_SWEEP_OUT: one line per change of the window (start, restart, end),
    /// and with GAUGE_AMSU_ALL every icon read too (kind@x:seconds, the line).</summary>
    [Fact]
    public void Amsu_in_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_AMSU") is not { } video) return;
        int size = int.Parse(Environment.GetEnvironmentVariable("GAUGE_AMSU_SIZE") ?? "100");
        int[] a = (Environment.GetEnvironmentVariable("GAUGE_AMSU_VP") ?? "0,0,1920,1080").Split(',').Select(int.Parse).ToArray();
        var vp = new Viewport(a[0], a[1], a[2], a[3]);
        var fit = new Fit(double.Parse(Environment.GetEnvironmentVariable("GAUGE_AMSU_HUD") ?? "100"));
        bool all = Environment.GetEnvironmentVariable("GAUGE_AMSU_ALL") is not null;
        var layout = AmsuLayout.Of(vp, fit, size);
        PixelRect text = Geometry.BossTextRow(vp, fit), strip = Geometry.BossDebuffStrip(vp, fit, size);
        int top = Math.Min(text.Y, strip.Y), bottom = Math.Max(text.Bottom, strip.Bottom);
        PixelRect In(PixelRect r) => r with { Y = r.Y - top };
        var watch = new AmsuWatch { Rate = Environment.GetEnvironmentVariable("GAUGE_AMSU_RATE") == "low" ? AmsuRate.낮음 : AmsuRate.높음 };
        int identified = 0;
        var spent = TimeSpan.Zero;   // the reads' own time (boss text, row, timers), not the decode
        var reads = new Dictionary<RowPass, int> { [RowPass.Frames] = 0, [RowPass.Timers] = 0 };
        // Where the time goes: identifications (template match), walks (kept bombs), timer passes.
        var cost = new Dictionary<string, (int N, TimeSpan T)> { ["walk"] = (0, TimeSpan.Zero), ["timers"] = (0, TimeSpan.Zero), ["boss text"] = (0, TimeSpan.Zero), ["resample"] = (0, TimeSpan.Zero) };
        void Add(string k, TimeSpan d) => cost[k] = (cost[k].N + 1, cost[k].T + d);
        var lines = new List<string>();
        foreach (var (t, band) in Frames(video, vp.X, top, vp.Width, bottom - top, fps: int.Parse(Environment.GetEnvironmentVariable("GAUGE_AMSU_FPS") ?? "10")))
        {
            var now = TimeSpan.FromSeconds(t);
            var (pass, bossText) = watch.Due(now, layout.Timed);
            if (pass == RowPass.None) continue;
            reads[pass]++;
            var sw = Stopwatch.StartNew();
            long? boss = bossText ? BossDigits.ReadBar(band.Crop(In(text) with { X = text.X - vp.X }), Geometry.Scale(vp, fit)).Hp?.Max : null;
            if (bossText) Add("boss text", sw.Elapsed);
            var rowStart = sw.Elapsed;
            var s = band.Crop(In(strip) with { X = strip.X - vp.X });
            if (Environment.GetEnvironmentVariable("GAUGE_AMSU_RESAMPLE") is not null)   // the strip to reference size alone, once per pass
            {
                sw.Stop();
                var rs = Stopwatch.StartNew();
                _ = DebuffReader.ToReference(s, layout.Scale);
                Add("resample", rs.Elapsed);
                sw.Start();
            }
            var (edges, _) = watch.Read(layout, pass, boss, s, now);
            spent += sw.Elapsed;
            if (watch.ClassifiedAt == now) identified++;
            string kind = pass == RowPass.Timers ? "timers" : watch.ClassifiedAt != now ? "walk" : watch.State.Up.Count > 0 ? "identify (bomb up)" : "identify (no bomb)";
            if (!cost.ContainsKey(kind)) cost[kind] = (0, TimeSpan.Zero);
            Add(kind, sw.Elapsed - rowStart);
            foreach (var e in edges) lines.Add($"{t:0.00} {e.Kind} {e.Change} {e.Left:0.0} (line {watch.Line})");
            if (all && pass == RowPass.Frames)
            {
                var view = Amsu.View(watch.State, now);
                var bombs = string.Join(" ", watch.LastRow?.Bombs.Select(b => $"{b.Kind}@{b.Slot}:{b.Seconds?.ToString() ?? "-"}") ?? []);
                lines.Add($"   {t:0.00} boss {(boss is null ? "-" : "ok")} line {watch.Line} [{bombs}] | {view.Shown} {view.Left:0.0}{(view.Seen ? "" : " unseen")}");
            }
        }
        lines.Add($"passes: {reads[RowPass.Frames]} frame, {reads[RowPass.Timers]} timer, {identified} identified, {spent.TotalMilliseconds:0} ms");
        foreach (var (k, (n, d)) in cost) lines.Add($"  {k}: {n} x {(n == 0 ? 0 : d.TotalMilliseconds / n):0.00} ms = {d.TotalMilliseconds:0} ms");
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }

    /// <summary>GAUGE_AMSU_BENCH: each step of a 암수 read timed alone over a video's frames (2 a second), each run 5 times over
    /// them so the times are steady: the strip resized, the walk on the line, an identification on the line, the boss's HP
    /// text. GAUGE_AMSU_SIZE, GAUGE_AMSU_VP, GAUGE_AMSU_HUD (default 100) as GAUGE_AMSU; the line is the placed one.</summary>
    [Fact]
    public void Amsu_steps_timed_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_AMSU_BENCH") is not { } video) return;
        int size = int.Parse(Environment.GetEnvironmentVariable("GAUGE_AMSU_SIZE") ?? "100");
        int[] a = (Environment.GetEnvironmentVariable("GAUGE_AMSU_VP") ?? "0,0,1920,1080").Split(',').Select(int.Parse).ToArray();
        int line = Geometry.BossDebuffLine;
        var vp = new Viewport(a[0], a[1], a[2], a[3]);
        var fit = new Fit(double.Parse(Environment.GetEnvironmentVariable("GAUGE_AMSU_HUD") ?? "100"));
        var layout = AmsuLayout.Of(vp, fit, size);
        PixelRect text = Geometry.BossTextRow(vp, fit), strip = Geometry.BossDebuffStrip(vp, fit, size);
        int top = Math.Min(text.Y, strip.Y), bottom = Math.Max(text.Bottom, strip.Bottom);
        var frames = Frames(video, vp.X, top, vp.Width, bottom - top, fps: 2)
            .Select(f => (Strip: f.Image.Crop(strip with { X = strip.X - vp.X, Y = strip.Y - top }), Text: f.Image.Crop(text with { X = text.X - vp.X, Y = text.Y - top })))
            .ToList();
        var sized = frames.Select(f => DebuffReader.ToReference(f.Strip, layout.Scale)).ToList();
        double scale = Geometry.Scale(vp, fit);
        var lines = new List<string> { $"{frames.Count} frames, strip {strip.Width}x{strip.Height}, scale {layout.Scale:0.###}" };
        void Time(string name, Action<int> step)
        {
            for (int i = 0; i < frames.Count; i++) step(i);   // warm up
            var sw = Stopwatch.StartNew();
            for (int run = 0; run < 5; run++) for (int i = 0; i < frames.Count; i++) step(i);
            lines.Add($"{name}: {sw.Elapsed.TotalMilliseconds / (5 * frames.Count):0.000} ms");
        }
        Time("resize strip", i => DebuffReader.ToReference(frames[i].Strip, layout.Scale));
        Time("walk on the line (sized)", i => AmsuReader.WalkIn(sized[i], layout.Scale, layout.Slots, line));
        Time("identify on the line (sized)", i => AmsuReader.LocateIn(sized[i], layout.Scale, layout.Slots, line));
        Time("boss HP text", i => BossDigits.ReadBar(frames[i].Text, scale));
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }

    /// <summary>The rectangle of every half second of the video (FFMPEG decodes it), with its video second.</summary>
    internal static IEnumerable<(double T, Image Image)> Frames(string video, int x, int y, int w, int h, int fps = 2)
    {
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("FFMPEG")!)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "-v", "error", "-i", video, "-vf", $"fps={fps},format=bgra,crop={w}:{h}:{x}:{y}", "-f", "rawvideo", "-pix_fmt", "bgra", "-" })
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
            yield return (i / (double)fps, new Image(w, h, buf));
        }
        ff.WaitForExit();
    }
}
