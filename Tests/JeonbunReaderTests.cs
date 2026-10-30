using System.Text;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The 전분 panel (spec § C Inputs), read from the user's 1080p clears, wipe and guardian raid (jeonbun_N: the 470×88 crop
/// at 1448,849): its timer and my row's four cells, dealer and support views. The panel is see-through: jeonbun_15 has a bright
/// flash behind the orange and purple cells.</summary>
public sealed class JeonbunReaderTests
{
    // Each crop's timer and cells as the game shows them ("–": nothing dealt yet).
    static readonly (string Timer, string[] Cells)[] Labels =
    [
        ("00:16", ["28.57억", "1.78억", "28.57억", "1.78억"]),
        ("00:51", ["57.35억", "1.12억", "57.35억", "1.12억"]),
        ("02:21", ["142.99억", "1.01억", "35.91억", "5,985.77만"]),
        ("02:29", ["164.89억", "1.10억", "57.76억", "9,628.23만"]),
        ("00:17", ["86.47억", "92.39%", "100.00%", "84.94%"]),
        ("00:38", ["48.64억", "1.28억", "48.64억", "1.28억"]),
        ("00:47", ["57.21억", "1.21억", "57.21억", "1.21억"]),
        ("03:03", ["185.28억", "1.01억", "70.31억", "1.17억"]),
        ("00:06", ["–", "–", "–", "–"]),
        ("01:17", ["758.30억", "99.11%", "99.53%", "61.09%"]),
        ("00:13", ["53.91억", "88.08%", "100.00%", "76.40%"]),
        ("00:21", ["137.46억", "95.06%", "99.98%", "88.51%"]),
        ("01:07", ["562.09억", "98.84%", "99.39%", "58.30%"]),
        ("00:26", ["169.67억", "96.51%", "99.98%", "62.46%"]),
        ("00:34", ["259.47억", "97.51%", "98.69%", "65.16%"]),
        ("01:20", ["785.78억", "99.15%", "99.55%", "62.56%"]),   // a bright flash behind the panel
        ("00:14", ["23.81억", "1.70억", "23.81억", "1.70억"]),     // a last 4 once read as 1
    ];

    static Image Panel(int i) => Fixture.Load($"jeonbun_{i}").Frames[0];

    // Labels[i] is crop jeonbun_i, except past 15: crops 16–18 are the ones that read nothing.
    static Image Labelled(int i) => Panel(i < 16 ? i : i + 3);

    public static IEnumerable<object[]> LabelIndexes => Enumerable.Range(0, Labels.Length).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(LabelIndexes))]
    public void Every_cell_and_the_timer_read_as_the_game_shows_them(int i)
    {
        Assert.Equal(Labels[i].Timer, JeonbunReader.TimerText(Labelled(i)));
        Assert.Equal(Labels[i].Cells, JeonbunReader.Cells(Labelled(i)));
    }

    [Theory]
    [InlineData(0, 16, 2_857_000_000L, 178_000_000L, false)]
    [InlineData(2, 141, 14_299_000_000L, 101_000_000L, false)]
    [InlineData(3, 149, 16_489_000_000L, 110_000_000L, false)]
    [InlineData(7, 183, 18_528_000_000L, 101_000_000L, false)]
    [InlineData(8, 6, 0L, 0L, false)]                                 // nothing dealt yet
    [InlineData(4, 17, 8_647_000_000L, 8_647_000_000L / 17, true)]    // support: 조력 피해량, DPS over the timer
    [InlineData(15, 80, 78_578_000_000L, 78_578_000_000L / 80, true)]
    [InlineData(22, 27, 28_325_000_000L, 1_049_000_000L, false)]      // columns of the player's choice: 시너지A 유효율 (a %) third
    public void A_reading_has_my_damage_dps_and_the_timer(int i, int timer, long damage, long dps, bool support)
    {
        Assert.Equal(new JeonbunReading(damage, dps, timer, support), JeonbunReader.Read(Panel(i)));
    }

    // The player picks the four columns and their order: each is known by its title; only 피해량 (조력 피해량) and DPS are read.
    [Theory]
    [InlineData(0, "피해량,DPS,기타,기타")]          // 피해량 · DPS · 1분 피해량 · 1분 DPS
    [InlineData(2, "피해량,DPS,기타,기타")]
    [InlineData(4, "조력피해량,기타,기타,기타")]     // 조력 피해량 · 낙인 · 공격 증가 · 피해 증가 유효율
    [InlineData(15, "조력피해량,기타,기타,기타")]
    [InlineData(22, "피해량,DPS,기타,기타")]         // 피해량 · DPS · 시너지A 유효율 · 1분 DPS
    public void Each_column_is_known_by_its_title(int i, string kinds) =>
        Assert.Equal(kinds.Split(','), JeonbunReader.Columns(Panel(i)).Select(k => k.ToString()));

    /// <summary>Fixture <paramref name="i"/> with column <paramref name="from"/>'s title (or whole column) copied over column
    /// <paramref name="to"/>'s, and with <paramref name="swap"/> the two exchanged.</summary>
    static Image Columns(int i, int from, int to, bool titleOnly = false, bool swap = false)
    {
        int[] x = [2, 118, 235, 352, 468];
        var src = Panel(i);
        var img = new Image(src.Width, src.Height, (byte[])src.Bgra.Clone());
        int y1 = titleOnly ? 61 : img.Height, w = Math.Min(x[from + 1] - x[from], x[to + 1] - x[to]);
        void Move(int a, int b)
        {
            for (int y = 30; y < y1; y++)
                src.Bgra.AsSpan(y * src.Stride + x[a] * 4, w * 4).CopyTo(img.Bgra.AsSpan(y * img.Stride + x[b] * 4));
        }
        Move(from, to);
        if (swap) Move(to, from);
        return img;
    }

    [Fact]
    public void Columns_in_another_order_read_the_same() =>   // DPS and 1분 피해량 swapped
        Assert.Equal(new JeonbunReading(14_299_000_000, 101_000_000, 141, false), JeonbunReader.Read(Columns(2, 1, 2, swap: true)));

    [Fact]
    public void Without_a_dps_column_dps_is_the_damage_over_the_timer() =>
        Assert.Equal(new JeonbunReading(14_299_000_000, 14_299_000_000 / 141, 141, false), JeonbunReader.Read(Columns(2, 2, 1)));

    [Fact]
    public void Without_a_damage_column_damage_is_dps_times_the_timer() =>   // 1분 DPS titled where 피해량 was
        Assert.Equal(new JeonbunReading(101_000_000L * 141, 101_000_000, 141, false), JeonbunReader.Read(Columns(2, 3, 0, titleOnly: true)));

    [Theory]
    [InlineData(20, 1)]   // 01:28, a saved record above (grey), mine below (green)
    [InlineData(21, 0)]   // 02:24, mine above: the rows go by damage
    [InlineData(0, 0)]    // one row, mine
    [InlineData(17, null)]   // the 보관 기록 view: no row of mine
    [InlineData(22, 1)]      // 1080p, a saved record above
    public void With_a_saved_record_to_compare_my_row_is_the_green_one(int i, int? row)
    {
        // jeonbun_20–21: 종막 [EX 나이트메어] at 1440p 21:9, streamed and shown on 1080p (0.75 and blurred): too soft for its
        // digits, but the rows are plain.
        Assert.Equal(row, JeonbunReader.MyRow(Panel(i)));
    }

    [Theory]
    [InlineData(16)]   // not the panel (a lobby view where it sits later)
    [InlineData(17)]   // the 보관 기록 view before a fight: a message where the values go
    [InlineData(18)]   // the inventory over the panel
    public void Anything_but_my_row_and_a_timer_reads_nothing(int i)
    {
        Assert.Null(JeonbunReader.Read(Panel(i)));
    }

    [Fact]
    public void Amounts_are_in_the_games_units()
    {
        Assert.Equal(59_857_700L, JeonbunReader.Amount("5,985.77만"));
        Assert.Equal(2_857_000_000L, JeonbunReader.Amount("28.57억"));
        Assert.Equal(0L, JeonbunReader.Amount("–"));
        Assert.Null(JeonbunReader.Amount("28.5억"));   // the game always shows two decimals: a misread
        Assert.Null(JeonbunReader.Amount("99.11%"));   // a share, not an amount
    }

    [Fact]
    public void A_blank_or_upside_down_panel_reads_nothing()
    {
        Assert.Null(JeonbunReader.Read(new Image(470, 88, new byte[470 * 88 * 4])));
        var p = Panel(0);
        var px = new byte[p.Width * p.Height * 4];
        for (int y = 0; y < p.Height; y++)
            for (int x = 0; x < p.Width; x++)
            {
                var (b, g, r) = p.Pixel(x, p.Height - 1 - y);
                int at = (y * p.Width + x) * 4;
                (px[at], px[at + 1], px[at + 2], px[at + 3]) = (b, g, r, 255);
            }
        Assert.Null(JeonbunReader.Read(new Image(p.Width, p.Height, px)));
    }

    /// <summary>Writes Core/Vision/JeonbunTemplates.cs from the labelled crops (GAUGE_BUILD_TEMPLATES=1).</summary>
    [Fact]
    public void Build_jeonbun_templates_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TEMPLATES") != "1") return;
        var timer = new SortedDictionary<char, List<float[]>>();
        var value = new SortedDictionary<char, List<float[]>>();
        var bad = new List<string>();
        for (int i = 0; i < Labels.Length; i++)
        {
            Collect(timer, JeonbunReader.TimerGlyphs(Labelled(i)), Labels[i].Timer, $"{i} timer", bad);
            for (int c = 0; c < 4; c++)
                if (Labels[i].Cells[c] != "–") Collect(value, JeonbunReader.CellGlyphs(Labelled(i), c), Labels[i].Cells[c], $"{i} cell {c}", bad);
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        if (Environment.GetEnvironmentVariable("GAUGE_TRAIN_DIR") is { } dir)
            foreach (var video in Directory.GetFiles(dir, "*.mp4")) Train(video, timer, value);
        var sb = new StringBuilder();
        sb.AppendLine("namespace GaugeOverlay.Core.Vision;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The 전분 panel's glyphs (JeonbunReader), 7×11 grids of grey (DigitReader.GreyRamp), each the mean of every sample");
        sb.AppendLine("/// in the labelled jeonbun crops. Generated by JeonbunReaderTests.Build_jeonbun_templates_when_requested");
        sb.AppendLine("/// (GAUGE_BUILD_TEMPLATES=1); do not edit by hand.</summary>");
        sb.AppendLine("internal static class JeonbunTemplates");
        sb.AppendLine("{");
        Emit(sb, "Timer", timer);
        Emit(sb, "Value", value);
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(RepoRoot(), "Core", "Vision", "JeonbunTemplates.cs"), sb.ToString());
    }

    /// <summary>Writes Core/Vision/JeonbunTitles.cs from the fixtures' column titles (GAUGE_BUILD_TITLES=1): the standard
    /// dealer titles on every dealer crop, the support ones on the support crops, and jeonbun_22's 시너지A 유효율.</summary>
    [Fact]
    public void Build_jeonbun_title_templates_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TITLES") != "1") return;
        string[] dealer = ["피해량", "DPS", "1분 피해량", "1분 DPS"], support = ["조력 피해량", "낙인 유효율", "공격 증가 유효율", "피해 증가 유효율"];
        var crops = Enumerable.Range(0, Labels.Length).Select(i => (Image: Labelled(i), Titles: Labels[i].Cells[1].EndsWith('%') ? support : dealer)).ToList();
        crops.Add((Panel(22), ["", "", "시너지A 유효율", ""]));   // only its new title: its crop sits a little off the others
        var samples = new SortedDictionary<string, List<float[]>>(StringComparer.Ordinal);
        foreach (var (img, titles) in crops)
            for (int c = 0; c < 4; c++)
            {
                if (titles[c] == "") continue;
                var t = JeonbunReader.TitleGrid(img, c);
                Assert.True(t is not null, $"no title in column {c}");
                if (!samples.TryGetValue(titles[c], out var list)) samples[titles[c]] = list = [];
                list.Add(t!);
            }
        var sb = new StringBuilder();
        sb.AppendLine("namespace GaugeOverlay.Core.Vision;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The 전분 panel's column titles (JeonbunReader.Columns): each title box as a TitleW×TitleH grid of grey");
        sb.AppendLine("/// (DigitReader.GreyRamp), the mean of every sample in the jeonbun crops. Generated by");
        sb.AppendLine("/// JeonbunReaderTests.Build_jeonbun_title_templates_when_requested (GAUGE_BUILD_TITLES=1); do not edit by hand.</summary>");
        sb.AppendLine("internal static class JeonbunTitles");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly (string Name, string[] Rows)[] All =");
        sb.AppendLine("    [");
        foreach (var (name, list) in samples)
        {
            var rows = Enumerable.Range(0, JeonbunReader.TitleH).Select(y => new string([.. Enumerable.Range(0, JeonbunReader.TitleW).Select(x =>
                DigitReader.GreyRamp[Math.Clamp((int)(list.Average(g => g[y * JeonbunReader.TitleW + x]) / 25.6), 0, 9)])]));
            sb.AppendLine($"        (\"{name}\", [{string.Join(", ", rows.Select(r => $"\"{r}\""))}]),");
        }
        sb.AppendLine("    ];");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(RepoRoot(), "Core", "Vision", "JeonbunTitles.cs"), sb.ToString());
    }

    /// <summary>More samples from a whole 1080p video (GAUGE_TRAIN_DIR, FFMPEG): every half second's timer, labelled with the
    /// value the timer must show then (the video second plus the local offset most reads agree on; frames far from it, a frozen
    /// timer, are left out), and in a fight's first minute the DPS and 피해량 cells, which equal the 1분 cells read beside them.</summary>
    static void Train(string video, SortedDictionary<char, List<float[]>> timer, SortedDictionary<char, List<float[]>> value)
    {
        var frames = JeonbunSweep.Frames(video, 1448, 849, 470, 88).ToList();
        var reads = frames.Select(f => JeonbunReader.TimerText(f.Image) is { } t && t.Length == 5 && t[2] == ':'
            && int.TryParse(t[..2], out int m) && int.TryParse(t[3..], out int s) ? m * 60 + s : (int?)null).ToList();
        for (int i = 0; i < frames.Count; i++)
        {
            var near = Enumerable.Range(Math.Max(0, i - 20), Math.Min(frames.Count, i + 21) - Math.Max(0, i - 20))
                .Where(j => reads[j] is not null).Select(j => reads[j]!.Value - (int)Math.Floor(frames[j].T)).ToList();
            if (near.Count < 5 || reads[i] is not { } read) continue;
            int expected = (int)Math.Floor(frames[i].T) + near.GroupBy(d => d).MaxBy(g => g.Count())!.Key;
            if (Math.Abs(read - expected) > 9 || expected < 0) continue;
            var label = $"{expected / 60:00}:{expected % 60:00}";
            if (JeonbunReader.TimerGlyphs(frames[i].Image) is { Count: 5 } g) Collect(timer, g, label, "", []);
            if (expected >= 58) continue;
            var cells = JeonbunReader.Cells(frames[i].Image);
            for (int c = 0; c < 2; c++)
                if (cells[c + 2] is { } same && same != "–" && !same.EndsWith('%') && JeonbunReader.CellGlyphs(frames[i].Image, c) is { } cg
                    && cg.Count == same.Length)
                    Collect(value, cg, same, "", []);
        }
    }

    static void Collect(SortedDictionary<char, List<float[]>> into, IReadOnlyList<JeonbunReader.Glyph>? glyphs, string text, string where, List<string> bad)
    {
        if (glyphs is null || glyphs.Count != text.Length)
        {
            bad.Add($"{where} '{text}': {(glyphs is null ? "no line" : string.Join("", glyphs.Select(g => g.Mark ?? '#')))}");
            return;
        }
        foreach (var (g, ch) in glyphs.Zip(text))
            if (g.Grid is { } grid) { if (!into.TryGetValue(ch, out var list)) into[ch] = list = []; list.Add(grid); }
    }

    static void Emit(StringBuilder sb, string name, SortedDictionary<char, List<float[]>> samples)
    {
        sb.AppendLine($"    public static readonly (char Ch, string[] Rows)[] {name} =");
        sb.AppendLine("    [");
        foreach (var (ch, list) in samples)
        {
            var rows = Enumerable.Range(0, BossDigits.GridH).Select(y => new string([.. Enumerable.Range(0, BossDigits.GridW).Select(x =>
                DigitReader.GreyRamp[Math.Clamp((int)(list.Average(g => g[y * BossDigits.GridW + x]) / 25.6), 0, 9)])]));
            sb.AppendLine($"        ('{ch}', [{string.Join(", ", rows.Select(r => $"\"{r}\""))}]),");
        }
        sb.AppendLine("    ];");
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")) && !File.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
