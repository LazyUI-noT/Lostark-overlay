using System.Text;
using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Dev check, off by default (breaker-aden.plan.md Phase 2 design): for each 브레이커 clip, at 10 fps, what Core would
/// do with the orb as 아덴1 (SlotClassifier Label cue, CooldownTracker.Update + Advance, CastLog.IsCast) beside the raw gauge
/// read and what GaugeFilter shows (default mode, no key presses). One CSV per clip. Set GAUGE_BREAKER_SWEEP to the output
/// folder, GAUGE_BREAKER_CLIPS to the clips folder (reference/브레이커) and FFMPEG to ffmpeg.exe. The ready look
/// (SlotCalibration) is emulated as the fit would save it: from the 권왕 clip at 1.0 s (a full orb), shared by all clips; the *_nocal CSVs run without it.</summary>
public class BreakerSweep
{
    const int OriginX = 926, OriginY = 954, W = 66, H = 88;
    static readonly PixelRect Slot = new(931, 974, 55, 54);

    static GaugeDef Def(string folder, string mask) => GaugeLoader.Parse(
        File.ReadAllText(Path.Combine(Helpers.RepoRoot(), "Profiles", "aden", folder, GaugeLoader.JsonFile)), Fixture.Load(mask).Frames[0]);

    static Image Cut(Image area, PixelRect r) => area.Crop(new PixelRect(r.X - OriginX, r.Y - OriginY, r.Width, r.Height));

    [Fact]
    public void Sweep_the_clips_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BREAKER_SWEEP") is not { } outDir) return;
        var clips = Environment.GetEnvironmentVariable("GAUGE_BREAKER_CLIPS")!;
        Directory.CreateDirectory(outDir);
        var digits = new DigitReader(1.0);
        var kw = Path.Combine(clips, "권왕", "Lost Ark 2026.11.13 - 20.59.43.02.mp4");
        var ready = JeonbunSweep.Frames(kw, OriginX, OriginY, W, H, 10).First(f => Math.Abs(f.T - 1.0) < 0.01);
        var cal = new SlotCalibration(Cut(ready.Image, Slot).Copy());
        var runs = new (string Name, string Video, string Folder, string Mask)[]
        {
            ("kw", kw, "브레이커_권왕파천무", "gauge_breaker_kw_mask"),
            ("su_2057", Path.Combine(clips, "수라", "Lost Ark 2026.11.13 - 20.57.20.01.mp4"), "브레이커_수라의길", "gauge_breaker_su_mask"),
            ("su_act", Path.Combine(clips, "수라", "수라act.mp4"), "브레이커_수라의길", "gauge_breaker_su_mask"),
        };
        foreach (var (name0, video, folder, mask) in runs)
            foreach (bool calibrated in new[] { true, false })
        {
            var name = calibrated ? name0 : name0 + "_nocal";
            var def = Def(folder, mask);
            var filter = new GaugeFilter();
            var state = SlotState.Unknown(SlotId.아덴1);
            var csv = new StringBuilder("t,kind,flash,phase,cast,raw,shown\n");
            foreach (var (t, area) in JeonbunSweep.Frames(video, OriginX, OriginY, W, H, 10))
            {
                var at = TimeSpan.FromSeconds(t);
                var reading = SlotClassifier.Read(SlotId.아덴1, Cut(area, Slot), at, calibrated ? cal : null, digits, DarkCue.Label);
                var next = CooldownTracker.Advance(CooldownTracker.Update(state, reading), at);
                bool cast = CastLog.IsCast(state.Phase, next.Phase);
                state = next;
                double raw = def.Read(Cut(area, def.Ref));
                csv.Append($"{t:0.0},{reading.Kind},{(reading.Flash ? 1 : 0)},{state.Phase},{(cast ? 1 : 0)},{raw:0.00},{filter.Add(raw, at):0.00}\n");
            }
            File.WriteAllText(Path.Combine(outDir, name + ".csv"), csv.ToString());
        }
    }

    /// <summary>Dev check, off by default: the ring's halves (기력·충격, Profiles/aden/브레이커_ring_*) as the engine reads them, at
    /// 10 reads a second through its GaugeFilter (default mode, Run 3), over the 브레이커 clips at HUD 110. One CSV per clip and half:
    /// t,raw,shown, to see how far the shown bar trails the ring. GAUGE_RING_SWEEP: output folder; GAUGE_BREAKER_CLIPS, FFMPEG as above.</summary>
    [Fact]
    public void Sweep_the_ring_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_RING_SWEEP") is not { } outDir) return;
        var clips = Environment.GetEnvironmentVariable("GAUGE_BREAKER_CLIPS")!;
        Directory.CreateDirectory(outDir);
        var runs = new (string Name, string Video)[]
        {
            ("kw", Path.Combine(clips, "권왕", "Lost Ark 2026.11.13 - 20.59.43.02.mp4")),
            ("su_2057", Path.Combine(clips, "수라", "Lost Ark 2026.11.13 - 20.57.20.01.mp4")),
            ("su_act", Path.Combine(clips, "수라", "수라act.mp4")),
        };
        foreach (var (name, video) in runs)
            foreach (var side in new[] { "left", "right" })
            {
                var def = Def($"브레이커_ring_{side}", $"gauge_breaker_ring_{side}_mask");
                var rect = Geometry.ToScreen(def.Ref, new Viewport(0, 0, 1920, 1080), new Fit(110));
                var filter = new GaugeFilter { Run = 3 };   // as the engine's ring filters
                var csv = new StringBuilder("t,raw,shown\n");
                foreach (var (t, img) in JeonbunSweep.Frames(video, rect.X, rect.Y, rect.Width, rect.Height, 10))
                {
                    double raw = def.Read(img);
                    csv.Append($"{t:0.0},{raw:0.00},{filter.Add(raw, TimeSpan.FromSeconds(t)):0.00}\n");
                }
                File.WriteAllText(Path.Combine(outDir, $"{name}_{side}.csv"), csv.ToString());
            }
    }
}
