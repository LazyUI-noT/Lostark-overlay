using System.Text;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The boss bar's "X n" and "current/max" HP (BossDigits) on the bosstop fixtures. The templates come from the two
/// HUD 110 shots (mordum, layout); the 21:9 shot and the dummy at HUD 100 and 80 are read with templates never cut from them.
/// Margins on the five: the worst right digit scores 0.66 (dummy, HUD 80), 0.12 above its runner-up; the '/' wins by 0.10+;
/// the X scores 0.89+ and no digit reaches 0.29 against it.</summary>
public class BossDigitsTests
{
    static (Image Hp, Image Count, double Scale) Areas(string name, int top, int height, double hud)
    {
        var f = Fixture.Load(name);
        var (vp, fit) = (new Viewport(0, top, 1920, height), new Fit(hud));
        PixelRect Local(PixelRect r) => r with { X = r.X - f.Rect[0], Y = r.Y - f.Rect[1] };
        return (f.Frames[0].Crop(Local(Geometry.BossHpText(vp, fit))), f.Frames[0].Crop(Local(Geometry.BossCount(vp, fit))), Geometry.Scale(vp, fit));
    }

    // The dummy at HUD 80 was shot at full HP (100,000,000,000/100,000,000,000), unlike the HUD 100 shot.
    public static TheoryData<string, int, int, double, long, long, int?> Shots => new()
    {
        { "bosstop_mordum", 0, 1080, 110, 16_897_047_862_607, 17_126_566_042_900, 494 },
        { "bosstop_layout", 0, 1080, 110, 27_934_993_622, 29_863_807_555, 468 },
        { "bosstop_219", 135, 810, 110, 27_934_993_622, 29_863_807_555, 468 },
        { "bosstop_dummy100", 0, 1080, 100, 99_981_084_598, 100_000_000_000, null },
        { "bosstop_dummy80", 0, 1080, 80, 100_000_000_000, 100_000_000_000, null },
    };

    /// <summary>The whole bar row (Geometry.BossTextRow): "Lv.60", the HP and "X n" found wherever the bar starts and ends. A
    /// friend's 2560×1080 game (21:9, HUD 110) puts a shorter bar further left than the 16:9 shots, past the fixed areas.</summary>
    public static TheoryData<string, int, int, int, double, long, long, int?> BarShots => new()
    {
        { "bosstop_mordum", 0, 1920, 1080, 110, 16_897_047_862_607, 17_126_566_042_900, 494 },
        { "bosstop_layout", 0, 1920, 1080, 110, 27_934_993_622, 29_863_807_555, 468 },
        { "bosstop_219", 135, 1920, 810, 110, 27_934_993_622, 29_863_807_555, 468 },
        { "bosstop_dummy100", 0, 1920, 1080, 100, 99_981_084_598, 100_000_000_000, null },
        { "bosstop_dummy80", 0, 1920, 1080, 80, 100_000_000_000, 100_000_000_000, null },
        { "bosstop_2560", 180, 2560, 1080, 110, 1_057_137_405_322, 1_197_954_508_761, 177 },
    };

    [Theory, MemberData(nameof(BarShots))]
    public void Reads_the_hp_and_the_count_from_the_whole_bar_row(string name, int top, int width, int height, double hud, long current, long max, int? count)
    {
        var f = Fixture.Load(name);
        var (vp, fit) = (new Viewport(0, top, width, height), new Fit(hud));
        var band = Geometry.BossTextRow(vp, fit);
        var bar = BossDigits.ReadBar(f.Frames[0].Crop(band with { X = band.X - f.Rect[0], Y = band.Y - f.Rect[1] }), Geometry.Scale(vp, fit));
        Assert.Equal((current, max), bar.Hp is { } hp ? (hp.Current, hp.Max) : default((long, long)?));
        Assert.Equal(count, bar.Count);
        var countOnly = BossDigits.ReadBar(f.Frames[0].Crop(band with { X = band.X - f.Rect[0], Y = band.Y - f.Rect[1] }), Geometry.Scale(vp, fit), hp: false);
        Assert.Equal((count, null), (countOnly.Count, countOnly.Hp));   // the 줄 수 창's poll: same count, HP not read
    }

    [Theory, MemberData(nameof(Shots))]
    public void Reads_the_hp_and_the_count(string name, int top, int height, double hud, long current, long max, int? count)
    {
        var (hp, box, scale) = Areas(name, top, height, hud);
        Assert.Equal((current, max), BossDigits.ReadHp(hp, scale));
        Assert.Equal(count, BossDigits.ReadCount(box, scale));
    }

    // The user's screenshot (HUD 110) of 모르둠 with a shield not added to the lines (the fill stays). The white bar of a shield
    // added to the lines has no lossless shot yet (the ones given were compressed: digits scored under 0.55).
    [Theory]
    [InlineData("bosshp_shield_mordum", 6_747_376_602_438, 17_126_566_042_900, 321_823_151_269)]
    public void Reads_a_shield_in_brackets_after_the_hp(string name, long current, long max, long shield)
    {
        var text = Fixture.Load(name).Frames[0];
        Assert.Equal((current, max, shield), BossDigits.ReadHpWithShield(text, 1.1));
        Assert.Equal((current, max), BossDigits.ReadHp(text, 1.1));
        Assert.Equal((current, max, shield), BossDigits.ReadBar(text, 1.1).Hp);   // the row read keeps "(+n)" with the HP
    }

    [Fact]
    public void The_row_read_keeps_a_shield_with_the_hp_and_the_count_apart()
    {
        // The shield shot's text with "X 321" set beside it, a word's gap away, as on the bar.
        var text = Fixture.Load("bosshp_shield_mordum").Frames[0];
        var (_, box, _) = Areas("bosstop_mordum", 0, 1080, 110);
        int gap = 40, h = Math.Max(text.Height, box.Height);
        var row = new Image(text.Width + gap + box.Width, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < row.Width; x++)
            {
                var src = x < text.Width ? (y < text.Height ? text.Pixel(x, y) : ((byte)0, (byte)0, (byte)0))
                    : x >= text.Width + gap && y < box.Height ? box.Pixel(x - text.Width - gap, y) : ((byte)0, (byte)0, (byte)0);
                int i = y * row.Stride + x * 4;
                (row.Bgra[i], row.Bgra[i + 1], row.Bgra[i + 2], row.Bgra[i + 3]) = (src.Item1, src.Item2, src.Item3, 255);
            }
        var bar = BossDigits.ReadBar(row, 1.1);
        Assert.Equal((6_747_376_602_438L, 17_126_566_042_900L, 321_823_151_269L), bar.Hp);
        Assert.Equal(494, bar.Count);
    }

    // 구원의 종탑 3단계 (1,142,325,546,571): the max's last '1' fades under MinGlyph as the fight goes on (0.56, 0.55-, 0.46)
    // while every other digit stays 0.81+. Strict, the whole HP drops; with the fight's max known, the max's glyphs are taken as
    // that max by position (a cursor over them too), so only a max of another digit count still fails.
    [Theory]
    [InlineData(0, 96_198_309_725L, true)]
    [InlineData(1, 89_677_997_033L, false)]
    [InlineData(2, 42_481_520_811L, false)]
    public void A_weak_digit_in_the_max_reads_when_the_max_is_known(int frame, long current, bool strictReads)
    {
        const long max = 1_142_325_546_571;
        var row = Fixture.Load("bosshp_jongtap_weakmax").Frames[frame];   // the whole text row, as Engine reads it
        Assert.Equal(strictReads, BossDigits.ReadBar(row, 1.1).Hp is not null);
        Assert.Equal((current, max, 0L), BossDigits.ReadBar(row, 1.1, knownMax: max).Hp);
        if (strictReads) return;
        Assert.Null(BossDigits.ReadBar(row, 1.1, knownMax: max / 10).Hp);   // fewer digits: still strict
        Assert.Equal((current, max - 1, 0L), BossDigits.ReadBar(row, 1.1, knownMax: max - 1).Hp);   // same layout: taken as known (Engine clears it per gate)
    }

    // A mouse arrow left over the max's digits (spots where the strict read drops, from a sweep over the row): with the max
    // known it still reads, and the current HP, untouched, reads as before.
    [Theory]
    [InlineData(388, 27)]
    [InlineData(434, 27)]
    [InlineData(452, 27)]
    public void A_cursor_over_the_max_reads_when_the_max_is_known(int tipX, int tipY)
    {
        const long max = 1_142_325_546_571;
        var row = WithCursor(Fixture.Load("bosshp_jongtap_weakmax").Frames[0], tipX, tipY);
        Assert.Null(BossDigits.ReadBar(row, 1.1).Hp);
        Assert.Equal((96_198_309_725L, max, 0L), BossDigits.ReadBar(row, 1.1, knownMax: max).Hp);
    }

    // The same over a max followed by a shield (모르둠): the "(+n)" after it still reads.
    [Theory]
    [InlineData(158, 3)]
    [InlineData(196, 9)]
    [InlineData(242, 0)]
    public void A_cursor_over_the_max_keeps_the_shield_when_the_max_is_known(int tipX, int tipY)
    {
        const long max = 17_126_566_042_900;
        var row = WithCursor(Fixture.Load("bosshp_shield_mordum").Frames[0], tipX, tipY);
        Assert.Null(BossDigits.ReadBar(row, 1.1).Hp);
        Assert.Equal((6_747_376_602_438L, max, 321_823_151_269L), BossDigits.ReadBar(row, 1.1, knownMax: max).Hp);
    }

    // A cursor over the shield's digits breaks their commas: the read stopped at the last full group ("(+321", "(+321,823")
    // with digits still after it. That is a cut-off shield, so no read, never a shorter shield.
    [Theory]
    [InlineData(312, -12)]
    [InlineData(342, -12)]
    [InlineData(372, -12)]
    public void A_cursor_over_the_shield_reads_nothing_rather_than_a_shorter_shield(int tipX, int tipY)
    {
        var row = WithCursor(Fixture.Load("bosshp_shield_mordum").Frames[0], tipX, tipY);
        Assert.Null(BossDigits.ReadBar(row, 1.1).Hp);
    }

    static readonly string[] Arrow =
    [
        "B", "BB", "BWB", "BWWB", "BWWWB", "BWWWWB", "BWWWWWB", "BWWWWWWB", "BWWWWWWWB", "BWWWWWWWWB",
        "BWWWWWWWWWB", "BWWWWWWBBBBB", "BWWWBWWB", "BWWBBWWB", "BWB BWWB", "BB  BWWB", "B    BWWB", "     BWWB", "      BB",
    ];

    // The Windows arrow cursor (B black, W white) with its tip at (tipX, tipY).
    static Image WithCursor(Image src, int tipX, int tipY)
    {
        var img = new Image(src.Width, src.Height, (byte[])src.Bgra.Clone());
        for (int r = 0; r < Arrow.Length; r++)
            for (int c = 0; c < Arrow[r].Length; c++)
            {
                int x = tipX + c, y = tipY + r;
                if (Arrow[r][c] == ' ' || x < 0 || y < 0 || x >= img.Width || y >= img.Height) continue;
                byte v = Arrow[r][c] == 'W' ? (byte)255 : (byte)0;
                int i = y * img.Stride + x * 4;
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (v, v, v, 255);
            }
        return img;
    }

    [Fact]
    public void A_blank_or_bare_bar_reads_nothing()
    {
        var blank = new Image(580, 20);
        Assert.Null(BossDigits.ReadHp(blank, 1));
        Assert.Null(BossDigits.ReadCount(blank, 1));
        var fill = new Image(580, 20);
        for (int i = 0; i < fill.Bgra.Length; i += 4) (fill.Bgra[i], fill.Bgra[i + 1], fill.Bgra[i + 2]) = (16, 99, 192);   // 모르둠's orange
        Assert.Null(BossDigits.ReadHp(fill, 1));
    }

    [Fact]
    public void The_count_and_the_hp_do_not_read_each_other()
    {
        var (hp, box, scale) = Areas("bosstop_mordum", 0, 1080, 110);
        Assert.Null(BossDigits.ReadCount(hp, scale));   // commas and no X
        Assert.Null(BossDigits.ReadHp(box, scale));     // no '/'
    }

    /// <summary>Writes Core/Vision/BossDigitTemplates.cs from the two HUD 110 shots (GAUGE_BUILD_TEMPLATES=1).</summary>
    [Fact]
    public void Build_boss_digit_templates_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TEMPLATES") != "1") return;
        var samples = new SortedDictionary<char, List<float[]>>();
        foreach (var (name, hpText, countText) in new[] { ("bosstop_mordum", "16,897,047,862,607/17,126,566,042,900", "X494"),
                                                          ("bosstop_layout", "27,934,993,622/29,863,807,555", "X468") })
        {
            var (hp, box, scale) = Areas(name, 0, 1080, 110);
            foreach (var (area, text) in new[] { (hp, hpText), (box, countText) })
            {
                var glyphs = BossDigits.Glyphs(area, scale)!;
                Assert.Equal(text.Length, glyphs.Count);
                foreach (var (g, ch) in glyphs.Zip(text))
                    if (!g.Comma) { if (!samples.TryGetValue(ch, out var list)) samples[ch] = list = []; list.Add(g.Grid!); }
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine("namespace GaugeOverlay.Core.Vision;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The boss bar's digits, '/' and 'X' (BossDigits), 7×11 grids of grey (DigitReader.GreyRamp), each the mean of every");
        sb.AppendLine("/// sample in bosstop_mordum and bosstop_layout. Generated by BossDigitsTests.Build_boss_digit_templates_when_requested");
        sb.AppendLine("/// (GAUGE_BUILD_TEMPLATES=1); do not edit by hand.</summary>");
        sb.AppendLine("internal static class BossDigitTemplates");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly (char Ch, string[] Rows)[] Glyphs =");
        sb.AppendLine("    [");
        foreach (var (ch, list) in samples)
        {
            var rows = Enumerable.Range(0, BossDigits.GridH).Select(y => new string([.. Enumerable.Range(0, BossDigits.GridW).Select(x =>
                DigitReader.GreyRamp[Math.Clamp((int)(list.Average(g => g[y * BossDigits.GridW + x]) / 25.6), 0, 9)])]));
            sb.AppendLine($"        ('{ch}', [{string.Join(", ", rows.Select(r => $"\"{r}\""))}]),");
        }
        sb.AppendLine("    ];");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(RepoRoot(), "Core", "Vision", "BossDigitTemplates.cs"), sb.ToString());
    }
}
