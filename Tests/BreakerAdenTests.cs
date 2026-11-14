using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>브레이커's 아덴 orb (both 직업각인, breaker-aden.plan.md Phase 0): what the slot readers make of the orb if it
/// were an 아덴1 slot. Measurements of the current code, not behaviour: Phase 1 picks its data from them. Fixtures are
/// 1920×1080 HUD 110 crops (x 926, y 954, 66×88) of the three clips and the four still shots (tools/make-fixtures.ps1).
/// breaker_kw (권왕): full 0–1 s; Z at 1.8 s, flash 2.8–3.0, teal 2.2–4 s, cooldown number 4–5.5 s, empty 6 s; filling
/// 9.2–17.8 s; full 18–20 s; Z again 20.8 s (teal 21.5), flash 25, number 27, empty 29. breaker_su: empty 0 s, filling 3–22 s,
/// full flash 22.5 s, full 23–24 s. breaker_act (수라, Z): full 1.2–6 s, Z flash 6.3 s, drain 7.2–11.1 s, empty 12 s.
/// Findings: with no cue every state reads Ready; the label cue and the Z letter tell full from filling/empty; the
/// teal state and the cooldown number read like a full orb to every cue but the hue; the digits are not read; the orb's
/// glow ring spoils the label cue while 수라's orb drains.</summary>
public class BreakerAdenTests
{
    const int OriginX = 926, OriginY = 954;
    // Candidate 아덴1 rects (reference px): Orb is the orb with room for its digits (the digits' baseline is 0.615 of the
    // height, 1007 here); Low is lower, so the key letter Z (y 1026–1036) sits in the label strip (the bottom third).
    static readonly PixelRect Orb = new(931, 974, 55, 54), Low = new(931, 984, 55, 56);
    static readonly PixelRect Letter = new(949, 1025, 19, 14), OrbMask = new(933, 976, 51, 51);
    static readonly DigitReader Digits = new(1.0);

    static Image At(string name, double t, PixelRect r)
    {
        var f = Fixture.Load(name);
        int i = Enumerable.Range(0, f.Pts.Count).First(k => Math.Abs(f.Pts[k].TotalSeconds - t) < 0.01);
        return f.Frames[i].Crop(new PixelRect(r.X - OriginX, r.Y - OriginY, r.Width, r.Height));
    }

    static ReadKind Kind(Image crop, DarkCue cue, SlotId slot = SlotId.아덴1) =>
        SlotClassifier.Read(slot, crop, default, null, Digits, cue).Kind;

    static double Share(Image img, Func<byte, byte, byte, bool> test)
    {
        int n = 0;
        for (int y = 0; y < img.Height; y++) for (int x = 0; x < img.Width; x++) { var (b, g, r) = img.Pixel(x, y); if (test(r, g, b)) n++; }
        return n / (double)(img.Width * img.Height);
    }

    /// <summary>The key letter Z's white share (luma ≥ 200): 0.09–0.133 when lit, 0 when grey.</summary>
    static double LetterWhite(string name, double t) => Share(At(name, t, Letter), (r, g, b) => (29 * b + 150 * g + 77 * r) / 256.0 >= 200);

    /// <summary>Median hue (degrees) of the saturated, bright pixels inside the orb's mask; -1 when there are none.</summary>
    static double MedianHue(string name, double t, string maskFixture)
    {
        var img = At(name, t, OrbMask);
        var mask = Fixture.Load(maskFixture).Frames[0];
        var hues = new List<double>();
        for (int y = 0; y < mask.Height; y++)
            for (int x = 0; x < mask.Width; x++)
            {
                if (mask.Bgra[y * mask.Stride + x * 4 + 3] == 0) continue;
                var (b, g, r) = img.Pixel(x, y);
                int max = Math.Max(r, Math.Max(g, b)), c = max - Math.Min(r, Math.Min(g, b));
                if (c < 60 || max < 120) continue;
                hues.Add((((max == r ? 60.0 * (g - b) / c : max == g ? 120 + 60.0 * (b - r) / c : 240 + 60.0 * (r - g) / c)) + 360) % 360);
            }
        hues.Sort();
        return hues.Count == 0 ? -1 : hues[hues.Count / 2];
    }

    static GaugeDef Def(string folder, string mask) => GaugeLoader.Parse(
        File.ReadAllText(Path.Combine(Helpers.RepoRoot(), "Profiles", "aden", folder, GaugeLoader.JsonFile)), Fixture.Load(mask).Frames[0]);

    static double Fill(GaugeDef def, string name, double t) => def.Read(At(name, t, def.Ref));

    public static TheoryData<string, double> FullOrbs => new()
    {
        { "breaker_kw", 0.0 }, { "breaker_kw", 1.0 }, { "breaker_kw", 18.5 }, { "breaker_kw", 20.0 }, { "breaker_still_kw_full", 0.0 },
        { "breaker_su", 23.0 }, { "breaker_su", 24.0 }, { "breaker_act", 1.2 }, { "breaker_act", 6.0 }, { "breaker_still_su_full", 0.0 },
    };

    public static TheoryData<string, double> EmptyOrbs => new()
    {
        { "breaker_kw", 6.0 }, { "breaker_kw", 29.0 }, { "breaker_still_kw_empty", 0.0 },
        { "breaker_su", 0.0 }, { "breaker_act", 12.0 }, { "breaker_still_su_empty", 0.0 },
    };

    /// <summary>A full glowing orb reads Ready by every cue (middle luma 129–160 against Icon's 100 and Label's 70) and its
    /// letter is white (0.09–0.133).</summary>
    [Theory, MemberData(nameof(FullOrbs))]
    public void A_full_orb_reads_ready_by_every_cue_and_its_letter_is_white(string name, double t)
    {
        foreach (var rect in new[] { Orb, Low })
            foreach (var cue in new[] { DarkCue.None, DarkCue.Icon, DarkCue.Label })
                Assert.Equal(ReadKind.Ready, Kind(At(name, t, rect), cue));
        Assert.InRange(LetterWhite(name, t), 0.08, 0.17);
    }

    /// <summary>An empty orb is a dark ball (middle luma 35–58): Icon and Label say Unavailable, no cue says Ready, and the letter is grey.</summary>
    [Theory, MemberData(nameof(EmptyOrbs))]
    public void An_empty_orb_reads_unavailable_by_Icon_and_Label_and_its_letter_is_grey(string name, double t)
    {
        foreach (var rect in new[] { Orb, Low })
        {
            Assert.Equal(ReadKind.Ready, Kind(At(name, t, rect), DarkCue.None));
            Assert.Equal(ReadKind.Unavailable, Kind(At(name, t, rect), DarkCue.Icon));
            Assert.Equal(ReadKind.Unavailable, Kind(At(name, t, rect), DarkCue.Label));
        }
        Assert.Equal(0, LetterWhite(name, t), 0.02);
    }

    /// <summary>수라's liquid (3–22 s, 3 % to 100 %): Icon and Label both say Unavailable until the orb is full, letter grey.</summary>
    [Theory]
    [InlineData(3.0)] [InlineData(6.0)] [InlineData(10.0)] [InlineData(14.0)] [InlineData(17.0)] [InlineData(20.0)] [InlineData(22.0)]
    public void A_filling_수라_orb_reads_unavailable_until_it_is_full(double t)
    {
        foreach (var cue in new[] { DarkCue.Icon, DarkCue.Label })
            Assert.Equal(ReadKind.Unavailable, Kind(At("breaker_su", t, Orb), cue));
        Assert.Equal(0, LetterWhite("breaker_su", t), 0.02);
    }

    /// <summary>권왕's liquid is brighter: Label says Unavailable all along (9.2–17.8 s), but Icon (middle luma 112–120) says
    /// Ready from about 90 % full (16 s, 17.8 s), and on the lower rect already at 50 % (12 s: 114). Label is the usable cue.</summary>
    [Fact]
    public void A_filling_권왕_orb_reads_unavailable_by_Label_but_the_Icon_cue_calls_it_ready_near_full()
    {
        foreach (double t in new[] { 9.2, 12.0, 16.0, 17.8 })
        {
            Assert.Equal(ReadKind.Unavailable, Kind(At("breaker_kw", t, Orb), DarkCue.Label));
            Assert.Equal(0, LetterWhite("breaker_kw", t), 0.02);
        }
        Assert.Equal(ReadKind.Unavailable, Kind(At("breaker_kw", 12.0, Orb), DarkCue.Icon));
        Assert.Equal(ReadKind.Ready, Kind(At("breaker_kw", 16.0, Orb), DarkCue.Icon));
        Assert.Equal(ReadKind.Ready, Kind(At("breaker_kw", 17.8, Orb), DarkCue.Icon));
        Assert.Equal(ReadKind.Ready, Kind(At("breaker_kw", 12.0, Low), DarkCue.Icon));
    }

    /// <summary>The use flash washes the orb out (middle luma 231–251): with no calibration the slot reads CooldownIllegible,
    /// not Ready. Only two of the five flashes light the white ring (Flash): the use at 3.0 s (권왕) and the Z at 6.3 s (수라);
    /// the other 권왕 flashes, and 수라's flash on filling up (22.5 s), do not, so the ring is no use cue.</summary>
    [Theory]
    [InlineData("breaker_kw", 2.8, false)] [InlineData("breaker_kw", 3.0, true)] [InlineData("breaker_kw", 25.0, false)]
    [InlineData("breaker_su", 22.5, false)] [InlineData("breaker_act", 6.3, true)]
    public void A_flash_reads_illegible_and_only_some_light_the_flash_ring(string name, double t, bool ring)
    {
        var reading = SlotClassifier.Read(SlotId.아덴1, At(name, t, Orb), default, null, Digits, DarkCue.None);
        Assert.Equal(ReadKind.CooldownIllegible, reading.Kind);
        Assert.Equal(ring, reading.Flash);
    }

    /// <summary>권왕's teal state (2.4, 3.5, 4 s after Z; 21.5 s after the 2nd Z) reads as a full orb to every cue (Icon: middle
    /// luma 113–166; Label: letter and ring white), and the letter stays white until the orb empties (6 s). Its hue (141–164°)
    /// is far from the liquid's (22–38°), and neither class's `filling` colours match it (the gauge reads 0, not a part full).</summary>
    [Theory]
    [InlineData(2.4)] [InlineData(3.5)] [InlineData(4.0)] [InlineData(21.5)]
    public void The_teal_state_reads_like_a_full_orb_but_its_hue_is_not_the_liquid_s(double t)
    {
        foreach (var cue in new[] { DarkCue.None, DarkCue.Icon, DarkCue.Label })
            Assert.Equal(ReadKind.Ready, Kind(At("breaker_kw", t, Orb), cue));
        Assert.InRange(LetterWhite("breaker_kw", t), 0.08, 0.17);
        Assert.InRange(MedianHue("breaker_kw", t, "gauge_breaker_kw_mask"), 130, 175);
        Assert.Equal(0, Fill(Def("브레이커_권왕파천무", "gauge_breaker_kw_mask"), "breaker_kw", t), 0.02);
        Assert.Equal(0, Fill(Def("브레이커_수라의길", "gauge_breaker_su_mask"), "breaker_kw", t), 0.02);
    }

    /// <summary>The liquid's hue (orb at least a quarter full) is orange for 권왕 (22–29°) and red for 수라 (8–12°): the
    /// 9° gap the plan found, with the teal at 141–164° far off both.</summary>
    [Fact]
    public void The_liquid_s_median_hue_tells_the_two_직업각인_apart()
    {
        foreach (double t in new[] { 9.2, 12.0, 16.0, 17.8, 20.0 }) Assert.InRange(MedianHue("breaker_kw", t, "gauge_breaker_kw_mask"), 21, 30);
        foreach (double t in new[] { 10.0, 12.0, 14.0, 17.0, 20.0, 22.0, 23.0 }) Assert.InRange(MedianHue("breaker_su", t, "gauge_breaker_su_mask"), 7, 13);
    }

    /// <summary>The cooldown number sits over the teal orb (권왕 4–5.5 s, 27 s: "39s", "38s", "37s", the glyph about as large as
    /// a normal slot's). As an 아덴1 (the smaller 아덴 font) none of the four frames is read; with the normal font only 27 s
    /// reads right (39), 5.5 s reads a wrong 7 for 37, 5.0 s is Illegible and 4.0 s (bright teal) shows nothing. Digits over the
    /// orb are not reliable. With the cooldown number up, Icon says Unavailable (middle luma 84–93) at 5, 5.5 and 27 s but
    /// Ready at 4 s (158); Label says Ready throughout, the letter being white.</summary>
    [Fact]
    public void The_cooldown_number_over_the_teal_orb_is_mostly_not_read()
    {
        foreach (double t in new[] { 4.0, 5.0, 5.5, 27.0 })
        {
            Assert.Equal(ReadKind.Ready, Digits.Read(At("breaker_kw", t, Orb), SlotId.아덴1).Kind);
            Assert.Equal(ReadKind.Ready, Kind(At("breaker_kw", t, Orb), DarkCue.Label));
        }
        Assert.Equal((ReadKind.Seconds, 39), Digits.Read(At("breaker_kw", 27.0, Orb), SlotId.일반1));
        Assert.Equal((ReadKind.Seconds, 7), Digits.Read(At("breaker_kw", 5.5, Orb), SlotId.일반1));
        Assert.Equal(ReadKind.CooldownIllegible, Digits.Read(At("breaker_kw", 5.0, Orb), SlotId.일반1).Kind);
        Assert.Equal(ReadKind.Ready, Digits.Read(At("breaker_kw", 4.0, Orb), SlotId.일반1).Kind);
        foreach (double t in new[] { 5.0, 5.5, 27.0 }) Assert.Equal(ReadKind.Unavailable, Kind(At("breaker_kw", t, Orb), DarkCue.Icon));
        Assert.Equal(ReadKind.Ready, Kind(At("breaker_kw", 4.0, Orb), DarkCue.Icon));
    }

    /// <summary>수라's Z (수라act.mp4): the key letter goes grey in the flash frame itself (6.3 s) and stays grey for the whole
    /// drain and after, a clean use edge. The cues on a slot rect do not follow it: while the orb drains (7.2–9.6 s) its glow
    /// ring keeps the label strip white (share 0.024–0.032 against Label's 0.012) and the orb's middle bright, so Label on the
    /// lower rect reads Ready until 10.2 s, Icon on the orb rect until 8.4 s.</summary>
    [Fact]
    public void While_the_orb_drains_the_letter_is_grey_but_the_slot_cues_still_read_ready()
    {
        Assert.InRange(LetterWhite("breaker_act", 6.0), 0.08, 0.17);
        foreach (double t in new[] { 6.3, 6.6, 7.2, 7.8, 8.4, 9.0, 9.6, 10.2, 10.8, 11.4, 12.0 }) Assert.True(LetterWhite("breaker_act", t) < 0.05, $"{t} s {LetterWhite("breaker_act", t)}");
        foreach (double t in new[] { 7.2, 7.8, 8.4, 9.0, 9.6 }) Assert.Equal(ReadKind.Ready, Kind(At("breaker_act", t, Low), DarkCue.Label));
        foreach (double t in new[] { 6.6, 7.2, 7.8, 8.4 }) Assert.Equal(ReadKind.Ready, Kind(At("breaker_act", t, Orb), DarkCue.Icon));
        Assert.Equal(ReadKind.Unavailable, Kind(At("breaker_act", 10.8, Low), DarkCue.Label));
    }

    /// <summary>The orb's own fill reading (the committed masks): 수라's clip rises 3 % to 100 %, and the drain after Z falls from
    /// 74 % to 3 % in 3 s, so the gauge could follow the drain.</summary>
    [Theory]
    [InlineData("breaker_su", 8.0, 0.31)] [InlineData("breaker_su", 12.0, 0.55)] [InlineData("breaker_su", 17.0, 0.79)] [InlineData("breaker_su", 22.0, 1.0)]
    [InlineData("breaker_act", 7.8, 0.74)] [InlineData("breaker_act", 8.4, 0.60)] [InlineData("breaker_act", 9.0, 0.46)]
    [InlineData("breaker_act", 9.6, 0.31)] [InlineData("breaker_act", 10.2, 0.17)] [InlineData("breaker_act", 10.8, 0.03)]
    public void The_수라_mask_reads_the_filling_and_the_drain(string name, double t, double expected) =>
        Assert.Equal(expected, Fill(Def("브레이커_수라의길", "gauge_breaker_su_mask"), name, t), 0.05);

    // 4K and 4K forced 21:9 (HUD 100): the emblem area of the 바드 stills, not the orb itself.

    static readonly Viewport Uhd = new(0, 0, 3840, 2160), Uhd219 = new(0, 270, 3840, 1620);

    /// <summary>The emblem area (Geometry.RefEmblem) of the 4K stills is what the emblem_4k fixtures cut, and the orb rect and
    /// the candidate slot rects derived through Geometry sit inside it at the scale (1.82 and 1.36) and centred on it. 바드's
    /// crystals fill the same medallion: the left edge and bottom of their bounding box (the 4th crystal is empty, so not the right edge) lie on Geometry's rect for the 바드 gauge (±4 px), so the
    /// derived rects land on the same area of the picture.</summary>
    [Theory]
    [InlineData("emblem_4k_바드", 0, 1833, 1945, 175, 196, 1.818)]
    [InlineData("emblem_4k219_바드", 270, 1855, 1729, 131, 147, 1.364)]
    public void The_orb_rect_lands_on_the_emblem_at_4k_and_4k_21_9(string fixture, int vpY, int ex, int ey, int ew, int eh, double scale)
    {
        var vp = vpY == 0 ? Uhd : Uhd219;
        var fit = new Fit(100);
        var emblem = Geometry.ToScreen(Geometry.RefEmblem, vp, fit);
        Assert.Equal((ex, ey, ew, eh), (emblem.X, emblem.Y, emblem.Width, emblem.Height));
        foreach (var rect in new[] { OrbMask, Orb, Low })
        {
            var s = Geometry.ToScreen(rect, vp, fit);
            Assert.InRange(s.Width, rect.Width * scale - 1, rect.Width * scale + 1);
            Assert.True(s.X >= emblem.X && s.Y >= emblem.Y && s.Right <= emblem.Right && s.Bottom <= emblem.Bottom, $"{rect} → {s} outside {emblem}");
        }
        var orb = Geometry.ToScreen(OrbMask, vp, fit);
        Assert.InRange(orb.X + orb.Width / 2.0 - (emblem.X + emblem.Width / 2.0), -3, 0);   // the orb is 1.5 reference px left of the emblem centre

        var still = Fixture.Load(fixture).Frames[0];
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < still.Height; y++)
            for (int x = 0; x < still.Width; x++)
            {
                var (b, g, r) = still.Pixel(x, y);
                if (!(b >= 150 && r <= 90 && b > g + 30) && !(r >= 200 && g >= 170 && b <= 80)) continue;   // blue crystal, yellow crystal
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
            }
        var gauge = Geometry.ToScreen(new PixelRect(925, 979, 69, 66), vp, fit);
        const int Tol = 4;
        Assert.InRange(emblem.X + x0, gauge.X - Tol, gauge.X + Tol);
        Assert.InRange(emblem.Y + y1, gauge.Bottom - Tol, gauge.Bottom + Tol);
    }

    /// <summary>OrbHue reads the same liquid hue through the shipped gauge definitions (phase 4b): 권왕 above the split, 수라 below.</summary>
    [Fact]
    public void Orb_hue_through_the_gauge_definition_falls_on_each_side_of_the_split()
    {
        var kw = Def("브레이커_권왕파천무", "gauge_breaker_kw_mask");
        var su = Def("브레이커_수라의길", "gauge_breaker_su_mask");
        foreach (double t in new[] { 9.2, 12.0, 16.0, 17.8, 20.0 }) Assert.InRange(OrbHue.Median(At("breaker_kw", t, kw.Ref), kw)!.Value, OrbHue.Split, 30);
        foreach (double t in new[] { 10.0, 12.0, 14.0, 17.0, 20.0, 22.0, 23.0 }) Assert.InRange(OrbHue.Median(At("breaker_su", t, su.Ref), su)!.Value, 7, OrbHue.Split);
    }

    [Fact]
    public void Orb_hue_is_none_on_the_teal_state()
    {
        var kw = Def("브레이커_권왕파천무", "gauge_breaker_kw_mask");
        foreach (double t in new[] { 2.4, 3.5 }) Assert.Null(OrbHue.Median(At("breaker_kw", t, kw.Ref), kw));
    }

    [Fact]
    public void Orb_vote_picks_after_five_agreeing_reads_and_none_on_a_split()
    {
        var pick = ("수라의길", "권왕파천무");
        var v = OrbVote.Empty;
        for (int i = 0; i < OrbVote.Needed - 1; i++) v = v.Add(10);
        Assert.Null(v.Pick(pick));
        Assert.Equal("수라의길", v.Add(11).Pick(pick));
        Assert.Equal("권왕파천무", Enumerable.Repeat(25.0, OrbVote.Needed).Aggregate(OrbVote.Empty, (a, h) => a.Add(h)).Pick(pick));
        Assert.Null(v.Add(33).Pick(pick));   // one effect frame (수라act reaches ~33°) holds the verdict back
        Assert.Null(v.Add(null).Pick(pick));   // no liquid read (teal, flash): skipped, not counted
    }

    /// <summary>Four reads before a loading screen and one after it are no five reads a second apart: a gap starts the vote over.</summary>
    [Fact]
    public void Orb_vote_starts_over_after_a_gap()
    {
        var pick = ("수라의길", "권왕파천무");
        var v = OrbVote.Empty;
        for (int i = 0; i < OrbVote.Needed - 1; i++) v = v.Add(10, TimeSpan.FromSeconds(i));
        Assert.Null(v.Add(10, TimeSpan.FromSeconds(OrbVote.Needed - 1) + OrbVote.MaxGap + TimeSpan.FromSeconds(1)).Pick(pick));
        Assert.Equal("수라의길", v.Add(10, TimeSpan.FromSeconds(OrbVote.Needed - 1)).Pick(pick));
    }
}
