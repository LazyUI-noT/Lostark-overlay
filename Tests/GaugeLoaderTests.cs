using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>A gauge definition (Profiles/aden/README.md): gauge.json plus mask.png, made from empty and full shots by
/// --build-gauge, and read from a screen capture of any size.</summary>
public class GaugeLoaderTests
{
    // A 40×6 bar and, for the pieces case, four 6×6 orbs 4 px apart; the gauge blue, the rest dark.
    static bool Bar(int x, int y) => y is >= 1 and < 5;
    static bool Orb(int x, int y) => y is >= 0 and < 6 && x % 10 < 6;

    static Image Draw(int w, int h, Func<int, int, bool> inGauge, Func<int, int, bool> lit)
    {
        var img = new Image(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * img.Stride + x * 4;
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) =
                    inGauge(x, y) && lit(x, y) ? ((byte)230, (byte)150, (byte)30, (byte)255) : ((byte)40, (byte)40, (byte)40, (byte)255);
            }
        return img;
    }

    static Image Mask(Func<int, int, bool> inGauge) =>
        GaugeReader.Mask(Draw(40, 6, inGauge, (_, _) => false), Draw(40, 6, inGauge, (_, _) => true));

    /// <summary>A capture of the gauge at <paramref name="scale"/> × its reference size (e.g. 2 at 4K).</summary>
    static Image Capture(Func<int, int, bool> inGauge, Func<int, int, bool> lit, int scale) =>
        Draw(40 * scale, 6 * scale, (x, y) => inGauge(x / scale, y / scale), (x, y) => lit(x / scale, y / scale));

    [Fact]
    public void The_mask_keeps_only_the_pixels_that_change_with_their_filled_colour()
    {
        var mask = Mask(Bar);
        Assert.Equal((0, 0, 0, 0), (mask.Bgra[0], mask.Bgra[1], mask.Bgra[2], mask.Bgra[3]));   // row 0: not the gauge
        int i = 2 * mask.Stride;
        Assert.Equal((230, 150, 30, 255), (mask.Bgra[i], mask.Bgra[i + 1], mask.Bgra[i + 2], mask.Bgra[i + 3]));
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(2, 0.5)]   // captured at 4K: scaled down to the mask
    [InlineData(1, 0.0)]
    [InlineData(2, 1.0)]
    public void A_continuous_gauge_reads_its_fill_at_any_capture_size(int scale, double fill)
    {
        var def = GaugeLoader.Parse("""{ "origin": [900, 1000], "units": "continuous", "order": { "kind": "linear", "angleDeg": 0 } }""", Mask(Bar));
        Assert.Equal(new PixelRect(900, 1000, 40, 6), def.Ref);
        Assert.Equal(fill, def.Read(Capture(Bar, (x, _) => x < fill * 40, scale)), 0.03);
    }

    [Fact]
    public void A_pieces_gauge_reads_the_share_of_lit_pieces()
    {
        var def = GaugeLoader.Parse("""{ "origin": [0, 0], "units": "pieces" }""", Mask(Orb));
        Assert.Equal(0.75, def.Read(Capture(Orb, (x, _) => x < 30, 1)));
    }

    [Fact]
    public void A_bottom_to_top_order_and_the_tolerance_are_read()
    {
        bool Tall(int x, int y) => x is >= 18 and < 22;
        var def = GaugeLoader.Parse("""{ "origin": [0, 0], "units": "continuous", "order": { "kind": "linear", "angleDeg": 90 }, "tolerance": 20 }""", Mask(Tall));
        Assert.Equal(20, def.Tolerance);
        Assert.Equal(0.5, def.Read(Capture(Tall, (_, y) => y >= 3, 1)), 0.1);   // 6 rows: steps of 0.2
    }

    [Theory]
    [InlineData("""{ "units": "continuous" }""")]                            // no origin
    [InlineData("""{ "origin": [0, 0], "units": "percent" }""")]
    [InlineData("""{ "origin": [0, 0], "units": "continuous", "order": { "kind": "spiral" } }""")]
    [InlineData("""{ "origin": [0, 0], "units": "continuous", "order": { "kind": "angular" } }""")]   // no centre
    [InlineData("""{ "origin": [0, 0], "units": "continuous", "order": { "kind": "angular", "center": [1, 1], "clockwise": "yes" } }""")]
    [InlineData("""not json""")]
    [InlineData("""{ "origin": [0, 0], "units": "continuous", "slack": -1 }""")]
    [InlineData("""{ "origin": [0, 0], "units": "continuous", "slack": 1.5 }""")]
    [InlineData("""{ "origin": [0, 0], "units": "continuous", "slack": 11 }""")]
    public void A_bad_gauge_json_is_rejected(string json) =>
        Assert.Throws<InvalidDataException>(() => GaugeLoader.Parse(json, Mask(Bar)));

    [Fact]
    public void An_empty_mask_is_rejected() =>
        Assert.Throws<InvalidDataException>(() => GaugeLoader.Parse("""{ "origin": [0, 0], "units": "continuous" }""", new Image(4, 4)));

    [Fact]
    public void Build_cuts_the_mask_to_the_gauge_and_writes_a_definition_that_reads_it()
    {
        // 100×40 shots with the bar at (30, 20); a changed pixel outside the search region is left out.
        bool InBar(int x, int y) => x is >= 30 and < 70 && y is >= 21 and < 25;
        var empty = Draw(100, 40, InBar, (_, _) => false);
        var full = Draw(100, 40, (x, y) => InBar(x, y) || (x, y) == (95, 2), (_, _) => true);

        var (json, mask) = GaugeLoader.Build(empty, full, new PixelRect(10, 10, 80, 30));
        Assert.Equal((40, 4), (mask.Width, mask.Height));
        var def = GaugeLoader.Parse(json, mask);
        Assert.Equal(new PixelRect(30, 21, 40, 4), def.Ref);
        Assert.False(def.Pieces);
        Assert.Equal(0.5, def.Read(Draw(40, 4, (_, _) => true, (x, _) => x < 20)), 0.03);

        var (orbs, _) = GaugeLoader.Build(Draw(40, 6, Orb, (_, _) => false), Draw(40, 6, Orb, (_, _) => true), new PixelRect(0, 0, 40, 6));
        Assert.Contains("\"pieces\"", orbs);
    }

    [Theory]
    [InlineData(-1, 0, 10, 10)]
    [InlineData(0, 0, 11, 10)]
    [InlineData(0, 0, 0, 10)]
    [InlineData(5, 5, 100000, 100000)]
    public void Build_with_a_region_outside_the_shots_is_rejected(int x, int y, int w, int h) =>
        Assert.Throws<InvalidDataException>(() => GaugeLoader.Build(Draw(10, 10, Bar, (_, _) => false), Draw(10, 10, Bar, (_, _) => true), new PixelRect(x, y, w, h)));

    [Fact]
    public void Build_with_nothing_changed_is_rejected() =>
        Assert.Throws<InvalidDataException>(() => GaugeLoader.Build(Draw(10, 10, Bar, (_, _) => false), Draw(10, 10, Bar, (_, _) => false), new PixelRect(0, 0, 10, 10)));

    [Fact]
    public void The_subclass_folder_is_used_before_the_class_folder()
    {
        var dir = Directory.CreateTempSubdirectory("profiles-").FullName;
        try
        {
            Assert.Null(GaugeLoader.Folder(dir, "바드", "절실한구원"));
            Directory.CreateDirectory(Path.Combine(dir, "aden", "바드"));
            File.WriteAllText(Path.Combine(dir, "aden", "바드", GaugeLoader.JsonFile), "{}");
            Assert.Equal(Path.Combine(dir, "aden", "바드"), GaugeLoader.Folder(dir, "바드", "절실한구원"));
            Directory.CreateDirectory(Path.Combine(dir, "aden", "바드_절실한구원"));
            File.WriteAllText(Path.Combine(dir, "aden", "바드_절실한구원", GaugeLoader.JsonFile), "{}");
            Assert.Equal(Path.Combine(dir, "aden", "바드_절실한구원"), GaugeLoader.Folder(dir, "바드", "절실한구원"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>바드's crystals fill one after another, left to right: the full shot's first crystals over the empty shot
    /// read as their share (1 of 4 for 절실한구원, 1 and 2 of 3 for 진실된용맹).</summary>
    [Theory]
    [InlineData("바드_절실한구원", "gauge_bard_support", 4)]
    [InlineData("바드_진실된용맹", "gauge_bard_dealer", 3)]
    public void 바드_crystals_read_by_how_many_are_full(string folder, string fixture, int crystals)
    {
        var mask = Fixture.Load(fixture + "_mask").Frames[0];
        var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Profiles", "aden", folder, GaugeLoader.JsonFile)), mask);
        var empty = Fixture.Load(fixture + "_empty").Frames[0];
        var full = Fixture.Load(fixture + "_full").Frames[0];
        Assert.Equal(0, def.Read(empty), 0.05);
        Assert.Equal(1, def.Read(full), 0.05);

        // Where each crystal ends: the mask's runs of columns holding any opaque pixel.
        var ends = new List<int>();
        bool inside = false;
        for (int x = 0; x <= mask.Width; x++)
        {
            bool any = x < mask.Width && Enumerable.Range(0, mask.Height).Any(y => mask.Bgra[y * mask.Stride + x * 4 + 3] > 0);
            if (inside && !any) ends.Add(x);
            inside = any;
        }
        Assert.Equal(crystals, ends.Count);
        for (int k = 1; k < crystals; k++)
        {
            var part = empty.Copy();
            for (int y = 0; y < part.Height; y++) Array.Copy(full.Bgra, y * full.Stride, part.Bgra, y * part.Stride, ends[k - 1] * 4);
            Assert.Equal((double)k / crystals, def.Read(part), 0.05);
        }
    }

    /// <summary>A crystal fills in gold and turns blue once full: the gold shades in <c>filling</c> count as filled, so a
    /// crystal part way up reads as such (절실한구원gameplay.mp4). Expected: each crystal's gold top against the mask's
    /// rows, found by hue (red and green well above blue), not by the listed colours: empty; 1st crystal 32% and 67%;
    /// 1 full + 2nd 59%; 2 full + 3rd 27%; 1st 27% under a blue flash; 1st 35%.</summary>
    [Fact]
    public void 바드_reads_a_crystal_that_is_still_filling_in_gold()
    {
        var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Profiles", "aden", "바드_절실한구원", GaugeLoader.JsonFile)),
            Fixture.Load("gauge_bard_support_mask").Frames[0]);
        double[] expected = [0, 0.08, 0.17, 0.40, 0.57, 0.07, 0.09];
        var frames = Fixture.Load("gauge_bard_gold").Frames;
        for (int i = 0; i < frames.Count; i++) Assert.Equal(expected[i], def.Read(frames[i]), 0.03);
    }

    /// <summary>절실한구원gameplay.mp4 from 15.4 s at 10 fps: 3 crystals full and the 4th a little gold (0.78); the skill at
    /// 18.1 s spends only the full crystals and the gold left over drops into the 1st, so the gauge shows 0.06, not 0. The
    /// skill's lightning over the crystals reads full for a read or two until 19.7 s, so the drop shows once the reads
    /// settle, at 19.2 s; a lightning read never shows.</summary>
    [Fact]
    public void 바드_spends_only_the_full_crystals_and_keeps_the_rest()
    {
        var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Profiles", "aden", "바드_절실한구원", GaugeLoader.JsonFile)),
            Fixture.Load("gauge_bard_support_mask").Frames[0]);
        var filter = new GaugeFilter();
        var shown = Fixture.Load("gauge_bard_use").Frames.Select(f => filter.Add(def.Read(f))).ToList();
        for (int i = 25; i < 38; i++) Assert.Equal(0.78, shown[i], 0.02);            // 17.9 s to 19.1 s
        for (int i = 38; i < shown.Count; i++) Assert.Equal(0.06, shown[i], 0.02);   // from 19.2 s
    }

    /// <summary>bard bug.mp4: the fit put the gauge 2–3 px beside the crystals, which are 8 px wide, so full (blue) crystals
    /// read empty: 4 full, 1 full + a little gold, 1 full + half of the 2nd, 2 full + a little gold showed 1, 0, 0.38 and
    /// 0.03. Read within 3 px across the fill, they read right wherever the gauge lands within that.</summary>
    [Fact]
    public void 바드_crystals_read_right_with_the_gauge_a_few_pixels_off()
    {
        var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Profiles", "aden", "바드_절실한구원", GaugeLoader.JsonFile)),
            Fixture.Load("gauge_bard_support_mask").Frames[0]);
        double[] expected = [1, 0.25, 0.37, 0.50];
        var frames = Fixture.Load("gauge_bard_shift").Frames;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -3; dx <= 3; dx++)
                for (int i = 0; i < frames.Count; i++)
                {
                    var img = new Image(def.Ref.Width, def.Ref.Height);
                    for (int y = 0; y < img.Height; y++)
                        Array.Copy(frames[i].Bgra, (y + 4 + dy) * frames[i].Stride + (4 + dx) * 4, img.Bgra, y * img.Stride, img.Width * 4);
                    Assert.True(Math.Abs(expected[i] - def.Read(img)) <= 0.03, $"({dx},{dy}) frame {i}: {def.Read(img):0.00}");
                }
    }

    [Fact]
    public void Colours_that_also_count_as_filled_are_read()
    {
        var def = GaugeLoader.Parse("""{ "origin": [0, 0], "units": "continuous", "filling": ["#102030", "#FFE16E"] }""", Mask(Bar));
        Assert.Equal([0x102030, 0xFFE16E], def.Shape.Filling);
    }

    [Theory]
    [InlineData(""" "gold" """)]
    [InlineData(""" ["#12345"] """)]
    [InlineData(""" ["#GG0000"] """)]
    [InlineData(""" [1] """)]
    public void Bad_also_filled_colours_are_rejected(string value) =>
        Assert.Throws<InvalidDataException>(() => GaugeLoader.Parse($$"""{ "origin": [0, 0], "units": "continuous", "filling": {{value}} }""", Mask(Bar)));

    /// <summary>The shipped gauges (Profiles/aden, from the empty and full shots in reference/subclass): 발키리's orb (its
    /// colour differs per 직업각인) and 워로드's purple U around the shield (shared by both 직업각인). Each fills bottom to
    /// top, so the full shot's lower half over the empty shot's upper half reads about half.</summary>
    [Theory]
    [InlineData("발키리_빛의기사", "gauge_valk_dealer")]
    [InlineData("발키리_해방자", "gauge_valk_support")]
    [InlineData("워로드", "gauge_war")]
    public void A_shipped_gauge_reads_empty_full_and_a_lower_half(string folder, string fixture)
    {
        var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Profiles", "aden", folder, GaugeLoader.JsonFile)),
            Fixture.Load(fixture + "_mask").Frames[0]);
        var empty = Fixture.Load(fixture + "_empty").Frames[0];
        var full = Fixture.Load(fixture + "_full").Frames[0];
        var half = empty.Copy();
        int from = half.Height / 2 * half.Stride;
        Array.Copy(full.Bgra, from, half.Bgra, from, half.Bgra.Length - from);
        Assert.Equal(0, def.Read(empty), 0.05);
        Assert.Equal(1, def.Read(full), 0.05);
        Assert.Equal(0.5, def.Read(half), 0.1);
    }
}
