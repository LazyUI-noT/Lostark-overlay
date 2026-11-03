using GaugeOverlay.Core.Cooldown;
using System.Text;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Dev check, off by default (reinforced-reader.plan.md): one reader over a 1080p clip and its damaged copies
/// (tools/degrade.ps1), every half second. The original's reads are the truth; per copy, the share of the original's read
/// frames that the copy reads the same, with the normal and the 강화 reader. GAUGE_DEGRADE: the original; GAUGE_DEGRADE_KIND:
/// buff | jeonbun | bosshp; GAUGE_DEGRADE_COPIES: the copies (';'); GAUGE_SWEEP_OUT: the report; FFMPEG: ffmpeg.exe.</summary>
public sealed class ReinforcedSweep
{
    [Fact]
    public void Score_damaged_copies_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_DEGRADE") is not { } original) return;
        string kind = Environment.GetEnvironmentVariable("GAUGE_DEGRADE_KIND") ?? "jeonbun";
        var copies = (Environment.GetEnvironmentVariable("GAUGE_DEGRADE_COPIES") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var truth = Reads(original, kind, false);
        int known = truth.Count(x => x is not null);
        var report = new StringBuilder($"{kind}: {Path.GetFileName(original)}, {truth.Count} frames, {known} read\n");
        foreach (var copy in copies)
            foreach (bool reinforced in new[] { false, true })
            {
                var got = Reads(copy, kind, reinforced);
                int same = truth.Zip(got).Count(p => p.First is not null && Same(p.First, p.Second));
                int wrong = truth.Zip(got).Count(p => p.First is not null && p.Second is not null && !Same(p.First, p.Second));
                report.Append($"{Path.GetFileName(copy),-40} {(reinforced ? "강화" : "보통")}  same {100.0 * same / Math.Max(1, known),5:0.0}%  wrong {100.0 * wrong / Math.Max(1, known),5:0.0}%\n");
                if (Environment.GetEnvironmentVariable("GAUGE_DEGRADE_SHOW") is not null)   // the frames that differ, to see how
                    foreach (var (t, (want, have)) in truth.Zip(got).Select((p, i) => (i / double.Parse(Environment.GetEnvironmentVariable("GAUGE_DEGRADE_FPS") ?? "2"), p)).Where(x => x.p.First is not null && !Same(x.p.First, x.p.Second)).Take(int.TryParse(Environment.GetEnvironmentVariable("GAUGE_DEGRADE_SHOW"), out int n) && n > 1 ? n : 12))
                        report.Append($"    {t,5:0.0}s  {want}  |  {have ?? "-"}\n");
            }
        File.WriteAllText(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, report.ToString());
    }

    /// <summary>A fresh reader per video: the tracked kinds keep state from frame to frame.</summary>
    static List<string?> Reads(string video, string kind, bool reinforced)
    {
        var (a, read) = Reader(kind);
        int fps = int.Parse(Environment.GetEnvironmentVariable("GAUGE_DEGRADE_FPS") ?? "2");   // the app reads buffs ~5/s
        return [.. JeonbunSweep.Frames(video, a.X, a.Y, a.Width, a.Height, fps).Select((f, i) => read(f.Image, reinforced, TimeSpan.FromSeconds(i / (double)fps)))];
    }

    /// <summary>The same read: equal, or for buffs the same buffs with each timer within a second (the plan's bar).</summary>
    static bool Same(string a, string? b)
    {
        if (b is null) return false;
        if (a == b) return true;
        // HP: the meter counts it in 100만 (GAUGE_HP_TOL: more for the drops copy, whose frames sit up to 1/24 s off).
        if (a.Split('/') is [var c1, var m1] && b.Split('/') is [var c2, var m2])
            return m1 == m2 && Math.Abs(long.Parse(c1) - long.Parse(c2)) <= long.Parse(Environment.GetEnvironmentVariable("GAUGE_HP_TOL") ?? "1000000");
        var (x, y) = (a.Split(' '), b.Split(' '));
        return x.Length == y.Length && x.Zip(y).All(p => p.First == p.Second || p.First.Split(':') is [var k1, var t1] && p.Second.Split(':') is [var k2, var t2]
            && k1 == k2 && double.TryParse(t1, out var s1) && double.TryParse(t2, out var s2) && s1 >= 0 && s2 >= 0 && Math.Abs(s1 - s2) <= 1);
    }

    static Image Clean(Image img, bool reinforced) => !reinforced ? img : (Environment.GetEnvironmentVariable("GAUGE_CLEAN") ?? "sharpen") switch
    {
        "levels" => Reinforce.Levels(img),
        "both" => Reinforce.Clean(Reinforce.Levels(img)),
        _ => Reinforce.Clean(img),
    };

    /// <summary>The area each reader looks at in the reference clips (1080p, HUD 110) and its read as a comparable key.</summary>
    static (PixelRect Area, Func<Image, bool, TimeSpan, string?> Read) Reader(string kind)
    {
        var vp = new Viewport(0, 0, 1920, 1080);
        var fit = new Fit(110);
        switch (kind)
        {
            case "bosshp":
                double scale = Geometry.Scale(vp, fit);
                var hp = new BossHpSoftReader();
                return (Geometry.BossTextRow(vp, fit), (img, r, now) => (r ? hp.Read(img, scale, now) : BossDigits.ReadBar(img, scale).Hp) is var (c, m, _) ? $"{c}/{m}" : null);
            case "amsu":   // what the 암수 창 shows (AmsuWatch at its own pace), the clip's 보스 디버프 크기 110, HUD 110
            {
                var hud = new Fit(110);
                var layout = AmsuLayout.Of(vp, hud, 110);
                PixelRect text = Geometry.BossTextRow(vp, hud), strip = Geometry.BossDebuffStrip(vp, hud, 110);
                int top = Math.Min(text.Y, strip.Y), bottom = Math.Max(text.Bottom, strip.Bottom);
                PixelRect In(PixelRect x) => x with { Y = x.Y - top };
                var watch = new AmsuWatch();
                var bossHp = new BossHpSoftReader();
                double bossScale = Geometry.Scale(vp, hud);
                return (new PixelRect(0, top, 1920, bottom - top), (img, r, now) =>
                {
                    var (pass, bossText) = watch.Due(now, layout.Timed);
                    if (pass != RowPass.None)
                    {
                        var row = img.Crop(In(text));
                        long? boss = !bossText ? null : r ? bossHp.Read(row, bossScale, now)?.Max : BossDigits.ReadBar(row, bossScale).Hp?.Max;
                        var st = img.Crop(In(strip));
                        watch.Read(layout, pass, boss, r ? Clean(st, r) : st, now);
                    }
                    var view = Amsu.View(watch.State, now);
                    return view.Shown is { } k ? $"{k}:{Math.Round(view.Left)}" : null;
                });
            }
            case "buff" or "bufftrack":
                int size = int.Parse(Environment.GetEnvironmentVariable("GAUGE_DEGRADE_BUFF") ?? "120");   // buff_gameplay.mp4: 120%
                var area = Geometry.BuffArea(vp, fit, size, 7);
                var boxes = Geometry.BuffBoxes(vp, fit, size, 7);
                var reader = new BuffReader(Geometry.BuffScale(vp, size));
                PixelRect Local(PixelRect x) => x with { X = x.X - area.X, Y = x.Y - area.Y };
                var states = Enum.GetValues<BuffKind>().Select(k => BuffState.Absent(k, TimeSpan.Zero)).ToArray();
                bool track = kind == "bufftrack";
                return (area, (img, r, now) =>
                {
                    if (track)   // what the app shows: each buff's tracked time left (BuffTracker), from every box's read
                    {
                        var clean = Clean(img, r);
                        var reads = boxes.Select(b => BuffReader.Classify(clean, Local(b.Icon)) is { } k
                                ? new BuffReading(k, reader.ReadTimer(clean, Local(b.Timer)), now) : null)
                            .OfType<BuffReading>().ToList();
                        states = [.. states.Select(b => BuffTracker.Update(b, reads, now, true))];
                        var on = states.Where(b => b.Active).Select(b => $"{b.Kind}:{(b.EndsAt is null ? -1 : Math.Round(b.Remaining(now).TotalSeconds))}").ToList();
                        return on.Count == 0 ? null : string.Join(" ", on);
                    }
                    var cells = boxes.Select(b => BuffReader.Classify(Clean(img, r), Local(b.Icon)) is { } k
                        ? $"{k}:{(reader.ReadTimer(Clean(img, r), Local(b.Timer)) is { } s ? Math.Round(s) : -1)}" : "-").ToList();
                    return cells.All(c => c == "-") ? null : string.Join(" ", cells);
                });
            default:   // the 전분 panel where the 세르카 replay found it
                var soft = new JeonbunSoftReader();
                return (new PixelRect(1448, 847, 470, 114), (img, r, now) => (r ? soft.Read(img, now) : JeonbunReader.Read(img)) is { } j ? $"{j.Timer} {j.Damage} {j.Dps}" : null);
        }
    }
}
