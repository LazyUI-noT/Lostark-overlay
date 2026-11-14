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

    /// <summary>GAUGE_ADEN: a 1920×1080 video's 아덴 게이지 through the engine's filter at its 10 reads a second
    /// (breaker-aden.plan.md phase 3): GAUGE_ADEN_DEF the Profiles/aden folder (default 브레이커_수라의길), GAUGE_ADEN_MASK its mask fixture (default
    /// gauge_breaker_su_mask), GAUGE_ADEN_HUD (default 110), GAUGE_ADEN_DRAINS ("0": the filter without FollowsDrain). GAUGE_SWEEP_OUT: per read "t read shown", and "USE" where
    /// the gauge left full (GaugeFilter.TakeDrainedFromFull, counted by the engine as the draining skill's use).</summary>
    [Fact]
    public void Aden_in_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_ADEN") is not { } video) return;
        string folder = Path.Combine(Helpers.RepoRoot(), "Profiles", "aden", Environment.GetEnvironmentVariable("GAUGE_ADEN_DEF") ?? "브레이커_수라의길");
        var def = Core.Data.GaugeLoader.Parse(File.ReadAllText(Path.Combine(folder, Core.Data.GaugeLoader.JsonFile)),
            Fixture.Load(Environment.GetEnvironmentVariable("GAUGE_ADEN_MASK") ?? "gauge_breaker_su_mask").Frames[0]);
        var rect = Geometry.ToScreen(def.Ref, new Viewport(0, 0, 1920, 1080), new Fit(double.Parse(Environment.GetEnvironmentVariable("GAUGE_ADEN_HUD") ?? "110")));
        var filter = new Core.Data.GaugeFilter { FollowsDrain = Environment.GetEnvironmentVariable("GAUGE_ADEN_DRAINS") != "0" };
        var lines = new List<string>();
        foreach (var (t, img) in Frames(video, rect.X, rect.Y, rect.Width, rect.Height, fps: 10))
        {
            double read = def.Read(img), shown = filter.Add(read, TimeSpan.FromSeconds(t));
            lines.Add($"{t:0.0} {read:0.00} {shown:0.00}{(filter.TakeDrainedFromFull() ? " USE" : "")}");
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

    /// <summary>GAUGE_HOMEWORK: 숙제's fallback (<see cref="HomeworkWatch"/>, stepped once a second as the app does) over a video's boss
    /// bar, for what 테스트 모드 replays can't show (the fallback is off there). Nothing is stored. GAUGE_AMSU_HUD, GAUGE_AMSU_VP as
    /// GAUGE_AMSU. GAUGE_HOMEWORK_GATE: the gate id 자동 인식 would name (no OCR here). GAUGE_SWEEP_OUT: each new max HP, each decision, the bar's
    /// reads. Never blocked (the app holds while the HUD is hidden), so a cutscene counts as the bar gone.</summary>
    [Fact]
    public void Homework_in_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_HOMEWORK") is not { } video) return;
        int[] a = (Environment.GetEnvironmentVariable("GAUGE_AMSU_VP") ?? "0,0,1920,1080").Split(',').Select(int.Parse).ToArray();
        var vp = new Viewport(a[0], a[1], a[2], a[3]);
        var fit = new Fit(double.Parse(Environment.GetEnvironmentVariable("GAUGE_AMSU_HUD") ?? "100"));
        string profiles = Path.Combine(Helpers.RepoRoot(), "Profiles");
        var gates = GaugeOverlay.Core.Data.RaidData.Load(profiles);
        var raids = GaugeOverlay.Core.Data.HomeworkData.Load(profiles);
        var text = Geometry.BossTextRow(vp, fit);
        var nameGate = gates.FirstOrDefault(g => g.Id == Environment.GetEnvironmentVariable("GAUGE_HOMEWORK_GATE"));   // what 자동 인식 would name
        var watch = HomeworkWatch.None;
        var lines = new List<string>();
        WatchRead? latest = null;
        long lastMax = -1;
        int second = 0, frames = 0, reads = 0;
        void Step(int at)
        {
            var (next, decision) = watch.Step(latest, new WatchContext(false, nameGate, TimeSpan.FromSeconds(at)), gates, raids);
            watch = next;
            latest = null;
            if (decision is not null) lines.Add($"{at} DECISION {decision.Raid} {decision.Difficulty ?? "?"} {decision.Outcome}");
        }
        foreach (var (t, img) in Frames(video, text.X, text.Y, text.Width, text.Height))
        {
            frames++;
            if ((int)t != second) { Step(second); second = (int)t; }
            if (BossDigits.ReadBar(img, Geometry.Scale(vp, fit)).Hp is not { } hp) continue;
            reads++;
            latest = new WatchRead(hp.Current, hp.Max);
            if (hp.Max == lastMax) continue;
            lastMax = hp.Max;
            lines.Add($"{t:0.0} bar {hp.Current}/{hp.Max} gate {GaugeOverlay.Core.Data.RaidData.ByHp(gates, hp.Max)?.Gate.Id ?? "-"}");
        }
        for (int after = 0; after <= FightRecorder.BarGoneFor + 1; after++) Step(second + after);   // the video ends: the bar goes
        lines.Add($"frames {frames}, bar reads {reads}, watching {(watch.Now is null ? "nothing" : "a bar")}, last {watch.Last?.Fight.Current}/{watch.Last?.Fight.Max} gate {watch.Last?.Fight.Gate?.Id ?? "-"}");
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

    /// <summary>GAUGE_BUFF: the buff row read and tracked as the engine does (BuffReader, BuffTracker) at 10 frames a second.
    /// GAUGE_BUFF_HUD (default 100), GAUGE_BUFF_SIZE (버프 크기, default 100), GAUGE_BUFF_COUNT (boxes read, default 7),
    /// GAUGE_BUFF_FROM / _TO (video seconds printed, default all). GAUGE_SWEEP_OUT: per frame each box's kind:seconds and the
    /// 공증 bar (on/off, total, left).</summary>
    [Fact]
    public void Buffs_in_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUFF") is not { } video) return;
        var vp = new Viewport(0, 0, 1920, 1080);
        var fit = new Fit(double.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_HUD") ?? "100"));
        int size = int.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_SIZE") ?? "100");
        int count = int.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_COUNT") ?? "7");
        double from = double.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_FROM") ?? "0");
        double to = double.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_TO") ?? "1e9");
        var area = Geometry.BuffArea(vp, fit, size, count);
        var boxes = Geometry.BuffBoxes(vp, fit, size, count);
        PixelRect Local(PixelRect r) => r with { X = r.X - area.X, Y = r.Y - area.Y };
        var reader = new BuffReader(Geometry.BuffScale(vp, size));
        var states = Enum.GetValues<BuffKind>().Select(k => BuffState.Absent(k, TimeSpan.Zero)).ToArray();
        var lines = new List<string>();
        foreach (var (sec, img) in Frames(video, area.X, area.Y, area.Width, area.Height, fps: 10))
        {
            var now = TimeSpan.FromSeconds(sec);
            var readings = new List<BuffReading>();
            var cells = new List<string>();
            for (int i = 0; i < boxes.Length; i++)
            {
                var kind = BuffReader.Classify(img, Local(boxes[i].Icon));
                double? s = kind is null ? null : reader.ReadTimer(img, Local(boxes[i].Timer));
                if (kind is { } k) readings.Add(new(k, s, now));
                cells.Add(kind is null ? "-" : $"{kind}:{s}");
            }
            states = [.. states.Select(b => BuffTracker.Update(b, readings, now, true))];
            var g = states[(int)BuffKind.공증];
            if (sec >= from && sec <= to)
                lines.Add($"{sec:0.0} | {string.Join(" ", cells)} | 공증 {(g.Active ? "on" : "off")} total {g.Total?.TotalSeconds} left {g.Remaining(now).TotalSeconds:0.0}");
            if (sec > to) break;
        }
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }

    /// <summary>GAUGE_BUFF_CHANGE: perf-hotspots 3b's measurement. Every box's icon identified every frame (today) is the
    /// reference; then "identify only when the thumbnail moved more than T from the one last identified" is simulated for
    /// each T, with and without the re-check (0.2 s for the 6 right-most boxes, 0.5 s for the rest), and every frame whose
    /// kind differs from the reference is counted. Also: the thumbnail change frame to frame where the kind stayed (noise,
    /// glare) against where it changed, and the hue drift of an unchanged icon. Settings as GAUGE_BUFF, plus GAUGE_BUFF_VP
    /// ("x,y,w,h", default 0,0,1920,1080) and GAUGE_BUFF_FPS (default 10).</summary>
    [Fact]
    public void Buff_identify_on_change_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUFF_CHANGE") is not { } video) return;
        var v = (Environment.GetEnvironmentVariable("GAUGE_BUFF_VP") ?? "0,0,1920,1080").Split(',').Select(int.Parse).ToArray();
        var vp = new Viewport(v[0], v[1], v[2], v[3]);
        var fit = new Fit(double.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_HUD") ?? "100"));
        int size = int.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_SIZE") ?? "100");
        int count = int.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_COUNT") ?? "7");
        int fps = int.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_FPS") ?? "10");
        var area = Geometry.BuffArea(vp, fit, size, count);
        var icons = Geometry.BuffBoxes(vp, fit, size, count).Select(b => b.Icon with { X = b.Icon.X - area.X, Y = b.Icon.Y - area.Y }).ToArray();
        IdentifyOnChange<BuffKind?>(video, area, icons, BuffReader.Classify, fps, i => i >= icons.Length - 6 ? 0.2 : 0.5);
    }

    /// <summary>GAUGE_DEBUFF_CHANGE: <see cref="Buff_identify_on_change_when_requested"/> for your debuff row (icon-gates step 2),
    /// the re-check 0.5 s as today's <c>RowCadence.ClassifyEvery</c>. GAUGE_BUFF_VP / _HUD / _SIZE / _FPS as there.</summary>
    [Fact]
    public void Debuff_identify_on_change_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_DEBUFF_CHANGE") is not { } video) return;
        var v = (Environment.GetEnvironmentVariable("GAUGE_BUFF_VP") ?? "0,0,1920,1080").Split(',').Select(int.Parse).ToArray();
        var vp = new Viewport(v[0], v[1], v[2], v[3]);
        var fit = new Fit(double.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_HUD") ?? "100"));
        int size = int.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_SIZE") ?? "100");
        int fps = int.Parse(Environment.GetEnvironmentVariable("GAUGE_BUFF_FPS") ?? "10");
        var area = Geometry.DebuffArea(vp, fit, size);
        var icons = Geometry.DebuffBoxes(vp, fit, size).Select(b => b.Icon with { X = b.Icon.X - area.X, Y = b.Icon.Y - area.Y }).ToArray();
        IdentifyOnChange<DebuffKind?>(video, area, icons, DebuffReader.Classify, fps, _ => 0.5);
    }

    /// <summary>The identify-on-change simulation over one row's <paramref name="icons"/> (relative to <paramref name="area"/>):
    /// the reference is <paramref name="classify"/> on every box every frame.</summary>
    static void IdentifyOnChange<T>(string video, PixelRect area, PixelRect[] icons, Func<Image, PixelRect, T> classify, int fps, Func<int, double> recheckEvery)
    {
        var inner = icons.Select(BuffReader.Interior).ToArray();
        int n = icons.Length;
        var eq = EqualityComparer<T>.Default;

        // One pass over the video: per frame and box the reference kind, the thumbnail and the mean hue.
        var refKind = new List<T[]>();
        var thumbs = new List<float[][]>();
        var hues = new List<double[]>();
        foreach (var (_, img) in Frames(video, area.X, area.Y, area.Width, area.Height, fps))
        {
            refKind.Add([.. icons.Select(r => classify(img, r))]);
            thumbs.Add([.. inner.Select(r => BuffReader.Thumb(img.Crop(r)))]);
            hues.Add([.. inner.Select(r => SlotClassifier.ColourStats(img, r).MeanHue)]);
        }
        static double Mad(float[] a, float[] b) { double s = 0; for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]); return s / a.Length; }
        static string Pct(List<double> xs, double p) => xs.Count == 0 ? "-" : $"{xs.Order().ElementAt((int)Math.Min(xs.Count - 1, p * xs.Count)):0.0}";

        var lines = new List<string> { $"{video}: {refKind.Count} frames at {fps}/s, {n} boxes, area {area.Width}x{area.Height}" };

        // Frame to frame: kind kept (a buff) against kind changed.
        var same = new List<double>(); var changed = new List<double>(); var drift = new List<double>();
        for (int f = 1; f < refKind.Count; f++)
            for (int i = 0; i < n; i++)
            {
                double d = Mad(thumbs[f][i], thumbs[f - 1][i]);
                if (!eq.Equals(refKind[f][i], refKind[f - 1][i])) changed.Add(d);
                else if (refKind[f][i] is not null) { same.Add(d); drift.Add(BuffReader.HueDistance(hues[f][i], hues[f - 1][i])); }
            }
        lines.Add($"kind kept ({same.Count}): change p50 {Pct(same, .5)} p90 {Pct(same, .9)} p99 {Pct(same, .99)} max {Pct(same, 1)}; hue drift p50 {Pct(drift, .5)} p99 {Pct(drift, .99)} max {Pct(drift, 1)}");
        lines.Add($"kind changed ({changed.Count}): change min {Pct(changed, 0)} p10 {Pct(changed, .1)} p50 {Pct(changed, .5)}; lowest {string.Join(" ", changed.Order().Take(12).Select(x => $"{x:0.0}"))}");

        // The simulation: identify when moved past T from the last identified thumbnail, or (re-check on) when due.
        foreach (bool recheck in new[] { false, true })
            foreach (double threshold in new[] { 2.0, 4, 6, 8, 12, 16, 24 })
            {
                var kind = new T[n]; var last = new float[n][]; var at = new double[n]; var run = new int[n];
                int wrong = 0, identifies = 0, fixedByRecheck = 0, longest = 0;
                var wrongAt = new List<string>();
                for (int f = 0; f < refKind.Count; f++)
                {
                    double t = f / (double)fps;
                    for (int i = 0; i < n; i++)
                    {
                        bool moved = last[i] is null || Mad(thumbs[f][i], last[i]) > threshold;
                        bool due = recheck && t - at[i] >= recheckEvery(i) - 1e-9;
                        if (moved || due)
                        {
                            identifies++;
                            if (!moved && !eq.Equals(kind[i], refKind[f][i])) fixedByRecheck++;
                            (kind[i], last[i], at[i]) = (refKind[f][i], thumbs[f][i], t);
                        }
                        if (eq.Equals(kind[i], refKind[f][i])) { run[i] = 0; continue; }
                        wrong++; longest = Math.Max(longest, ++run[i]);
                        if (wrongAt.Count < 8) wrongAt.Add($"{t:0.0}s#{i + 1} {kind[i]?.ToString() ?? "-"}≠{refKind[f][i]?.ToString() ?? "-"}");
                    }
                }
                lines.Add($"T {threshold,4} re-check {(recheck ? "on " : "off")}: identifies {100.0 * identifies / (refKind.Count * n),5:0.0}%, wrong frames {wrong}, longest {longest}, re-check fixed {fixedByRecheck}{(wrongAt.Count > 0 ? "  " + string.Join(", ", wrongAt) : "")}");
            }
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
