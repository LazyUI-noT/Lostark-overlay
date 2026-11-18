using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using static GaugeOverlay.Tests.Helpers;

namespace GaugeOverlay.Tests;

/// <summary>Dev sweep (GAUGE_BOSS_GATE=1): the boss row's thumbnail change, same slot frame to frame against different icons.</summary>
public class BossGateSweep
{
    [Fact]
    public void Sweep_boss_row_thumbnail_change_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BOSS_GATE") != "1") return;
        var hud = new Fit(110);
        const int CropX = 900, CropY = 160;
        var f = Fixture.Load("bosstext_8090");
        var same = new List<(double D, int Sec, int Slot)>();
        var cross = new List<double>();
        List<float[]>? last = null;
        for (int sec = 0; sec < f.Frames.Count; sec++)
        {
            int size = BossTimerTests.SizeAt(sec);
            var s = Geometry.BossDebuffStrip(Fhd, hud, size);
            int left = Math.Max(s.X, CropX), top = Math.Max(s.Y, CropY);
            var cut = new PixelRect(left - CropX, top - CropY, Math.Min(s.Right, CropX + f.Rect[2]) - left, Math.Min(s.Bottom, CropY + f.Rect[3]) - top);
            var placed = AmsuLayout.Of(Fhd, hud, size);
            var slots = placed.Slots with { FirstX = placed.Slots.FirstX - (left - s.X) };
            int line0 = placed.Line - (top - s.Y);
            var r = DebuffReader.ToReference(f.Frames[sec].Crop(cut), placed.Scale);
            int line = AmsuReader.SnapIn(r, placed.Scale, slots, line0, Geometry.BossLineSnap) ?? line0;
            var filled = AmsuReader.WalkIn(r, placed.Scale, slots, line).Filled;
            int y = (int)Math.Round(line / placed.Scale);
            var thumbs = filled.Select(x => new PixelRect((int)Math.Round(x.Icon.X / placed.Scale), y, DebuffReader.BossIconSize, DebuffReader.BossIconSize))
                .Where(b => IconGate.Inside(r, b)).Select(b => { var t = new float[BuffReader.ThumbLength]; BuffReader.Thumb(r, BuffReader.Interior(b), t); return t; }).ToList();
            for (int i = 0; i < thumbs.Count; i++)
                for (int j = i + 1; j < thumbs.Count; j++) cross.Add(IconGate.Change(thumbs[i], thumbs[j]));
            if (last is not null && last.Count == thumbs.Count)
                for (int i = 0; i < thumbs.Count; i++) same.Add((IconGate.Change(last[i], thumbs[i]), sec, i));
            last = thumbs;
        }
        static string Q(IEnumerable<double> v) { var a = v.Order().ToArray(); return a.Length == 0 ? "-" : $"n {a.Length} min {a[0]:0.0} p50 {a[a.Length / 2]:0.0} p90 {a[(int)(a.Length * 0.9)]:0.0} max {a[^1]:0.0}"; }
        string over = string.Join(" ", same.Where(x => x.D > IconGate.Threshold).Select(x => $"{x.Sec}s#{x.Slot}:{x.D:0.0}"));
        Assert.Fail($"same slot {Q(same.Select(x => x.D))} | over 8: {over} | different icons {Q(cross)}");
    }
}
