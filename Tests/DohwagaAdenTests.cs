using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>도화가 (both 직업각인, Profiles/aden/도화가): three orbs, each a blue ring filling clockwise from the bottom and
/// turning gold once full. Z and C are case 2 and go dark under one full orb. 도화가support.mp4 at 10 fps (gauge_doh_play,
/// doh_Z, doh_C): the 1st ring fills 0–6.4 s and turns gold at 7.3 s; the 2nd ring is full (still blue) 16.7–19.9 s and
/// turns gold at 20 s; C spends both at 21.4 s; two orbs come back at 25.1 s; Z spends one at 27.3 s and one at 28.9 s.</summary>
public class DohwagaAdenTests
{
    static GaugeDef Def() => GaugeLoader.Parse(File.ReadAllText(Path.Combine(Helpers.RepoRoot(), "Profiles", "aden", "도화가", GaugeLoader.JsonFile)),
        Fixture.Load("gauge_doh_mask").Frames[0]);

    [Fact]
    public void The_shots_read_empty_and_full()
    {
        var def = Def();
        Assert.Equal(0, def.Read(Fixture.Load("gauge_doh_empty").Frames[0]), 0.02);
        Assert.Equal(1, def.Read(Fixture.Load("gauge_doh_full").Frames[0]), 0.02);
    }

    /// <summary>A ring counts by how far round it is; a full ring still blue stays short of its section, a gold orb is one.</summary>
    [Theory]
    [InlineData(1.0, 0.04)]
    [InlineData(3.5, 0.13)]
    [InlineData(5.5, 0.19)]
    [InlineData(6.8, 0.32)]    // 1st ring full, still blue
    [InlineData(8.0, 0.40)]    // 1st gold, 2nd ring begun
    [InlineData(13.0, 0.55)]
    [InlineData(22.0, 0.09)]   // after C
    [InlineData(26.0, 0.80)]
    [InlineData(27.5, 0.51)]   // after Z
    [InlineData(30.0, 0.18)]   // after Z again
    public void The_recording_reads_the_orbs_and_the_ring_filling(double t, double expected)
    {
        var f = Fixture.Load("gauge_doh_play");
        int i = Enumerable.Range(0, f.Pts.Count).First(k => Math.Round(f.Pts[k].TotalSeconds, 1) >= t);
        Assert.Equal(expected, Def().Read(f.Frames[i]), 0.02);
    }

    [Fact]
    public void A_full_ring_still_blue_is_not_a_full_orb()
    {
        var f = Fixture.Load("gauge_doh_play");
        var v = Def().Read(f.Frames[Enumerable.Range(0, f.Pts.Count).First(k => f.Pts[k].TotalSeconds >= 18.0)]);
        Assert.True(v is > 0.6 and < 0.6667, $"{v}");
    }

    /// <summary>도화가bug_aden.mp4 at 10 fps: the 1st orb turns gold at 5.6 s and a blue spark sits at the bottom of the
    /// empty 2nd orb, where its ring starts and ends, until the 2nd ring starts filling at 10 s. One orb, not two.</summary>
    [Fact]
    public void A_spark_at_the_start_of_an_empty_ring_is_not_a_full_ring()
    {
        var f = Fixture.Load("gauge_dohbug");
        var def = Def();
        for (int i = 60; i <= 100; i++) Assert.InRange(def.Read(f.Frames[i]), 0.33, 0.37);
    }

    static Image Cut(Image src, PixelRect r)
    {
        var img = new Image(r.Width, r.Height);
        for (int y = 0; y < r.Height; y++) Array.Copy(src.Bgra, (r.Y + y) * src.Stride + r.X * 4, img.Bgra, y * img.Stride, r.Width * 4);
        return img;
    }

    /// <summary>도화가zc.png (만개, HUD about 100%, keys Z and X): a full gauge reads full and both 아덴 skills ready, at
    /// any fit near the HUD. The fit itself is not taken from this shot: its 각성기 frame glows red ("C+V"), which the
    /// solver reads as HUD 80.</summary>
    [Theory]
    [InlineData(95)]
    [InlineData(100)]
    [InlineData(105)]
    public void A_full_gauge_with_Z_and_X_lit_reads_full_and_ready(double hud)
    {
        var strip = Fixture.Load("dohzc_bottom").Frames[0];   // screen rows 900–1079
        var fit = new Fit(hud);
        PixelRect Local(PixelRect r) => new(r.X, r.Y - 900, r.Width, r.Height);
        var digits = new DigitReader(Geometry.Scale(Fhd, fit));
        var def = Def();
        Assert.Equal(1, def.Read(Cut(strip, Local(Geometry.ToScreen(def.Ref, Fhd, fit)))), 0.02);
        foreach (var slot in new[] { SlotId.아덴1, SlotId.아덴2 })
            Assert.Equal(ReadKind.Ready, SlotClassifier.Read(slot, Cut(strip, Local(Geometry.Slot(slot, Fhd, fit))), default, null, digits, DarkCue.Icon).Kind);
    }

    /// <summary>With a lit calibration (8.2 s), Z and C read dark while the gauge is under one orb and ready above it.</summary>
    [Fact]
    public void Z_and_C_are_dark_under_one_full_orb()
    {
        var digits = new DigitReader(Geometry.Scale(Fhd, new Fit(110)));
        (double From, double To)[] dark = [(0, 7.2), (21.4, 25.0), (29.0, 31.1)], lit = [(8.1, 20.5), (25.7, 28.0)];
        foreach (var (name, slot) in new[] { ("doh_Z", SlotId.아덴1), ("doh_C", SlotId.아덴2) })
        {
            var f = Fixture.Load(name);
            var cal = new SlotCalibration(f.Frames[82]);
            for (int i = 0; i < f.Frames.Count; i++)
            {
                double t = Math.Round(f.Pts[i].TotalSeconds, 1);
                var kind = SlotClassifier.Read(slot, f.Frames[i], f.Pts[i], cal, digits, DarkCue.Icon).Kind;
                if (dark.Any(s => t >= s.From && t <= s.To)) Assert.True(kind == ReadKind.Unavailable, $"{name} {t}s dark read {kind}");
                if (lit.Any(s => t >= s.From && t <= s.To)) Assert.True(kind == ReadKind.Ready, $"{name} {t}s lit read {kind}");
            }
        }
    }
}
