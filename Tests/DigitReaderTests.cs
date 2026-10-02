using System.Text;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class DigitReaderTests
{
    // Labelled HUD 110 crops the templates are cut from (fixture, frame, text shown).
    private static readonly (string Fixture, int Frame, string Text)[] Sources =
    [
        ("glyph_20s_W", 0, "20s"), ("glyph_22s_E", 0, "22s"), ("glyph_56s_T", 0, "56s"), ("glyph_23s_W", 0, "23s"),
        ("glyph_17s_W", 0, "17s"), ("glyph_54s_T", 0, "54s"), ("glyph_19s_W", 0, "19s"), ("glyph_18s_W", 0, "18s"),
        ("glyph_21s_E", 0, "21s"), ("cd_일반2", 0, "14s"), ("cd_초각스", 0, "41s"), ("cd_각성기", 0, "3m"),
        ("333_E", 35, "25s"), ("333_E", 45, "24s"),
    ];

    // Stack-count glyphs that differ from a shrunk timer glyph, cut from the bottom-right corner.
    private static readonly (string Fixture, int Frame, string Text)[] CountSources = [("lt2_count2_일반1", 0, "2")];

    [Fact]
    public void Yellow_counts_are_never_read()
    {
        // 각성기 in cd.png shows a yellow "99+" above the white "3m"; nomana's ready 각성기 shows only "99+".
        var digits = new DigitReader(1);
        Assert.Equal((ReadKind.Minutes, 3), digits.Read(Fixture.Load("cd_각성기").Frames[0], SlotId.각성기));
        Assert.Equal(ReadKind.Ready, digits.Read(Fixture.Load("nomana_각성기").Frames[0], SlotId.각성기).Kind);
    }

    // live_test2.mp4, HUD 110: "11s" was read as 41 or 1 on these slots; "31s" / "19s" as 34 / 49 after learning.
    [Theory]
    [InlineData("lt2_11_일반4", 11)]
    [InlineData("lt2_11_일반5", 11)]
    [InlineData("lt2_11_일반6", 11)]
    [InlineData("lt2_11_일반7", 11)]
    [InlineData("lt2_11_일반8", 11)]
    [InlineData("lt2_11_초각스", 11)]
    [InlineData("lt2_31_초각스", 31)]
    [InlineData("lt2_19_초각스", 19)]
    public void Reads_ones_next_to_ones(string name, int shown)
    {
        var f = Fixture.Load(name);
        Assert.Equal((ReadKind.Seconds, shown), new DigitReader(1).Read(f.Frames[0], Enum.Parse<SlotId>(f.Slot)));
    }

    // live test.mp4, HUD 110: the edge of the dark cooldown sweep just left of a single digit matched the '1' template
    // at the cut-off, so "6s" read 16, "3s" 13 and "1s" 11.
    /// <summary>sBug.mp4 (S, HUD 110): the icon's light-cyan crystal edge left of the digit matches a tens '1' as bright
    /// as the text in min(R, G, B), so "2s" read 12 and "1s" read 11 (shown as 10). Text is white; the edge is blue.</summary>
    [Fact]
    public void A_coloured_streak_is_not_a_tens_one()
    {
        var f = Fixture.Load("sbug_일반6");
        var digits = new DigitReader(Geometry.Scale(Fhd, new Fit(f.Hud)));
        for (int i = 0; i < f.Frames.Count; i++)
        {
            var (kind, value) = digits.Read(f.Frames[i], SlotId.일반6);
            Assert.True(kind != ReadKind.Seconds || value is 1 or 2, $"frame {i}: {kind} {value}");
        }
    }

    [Theory]
    [InlineData("lt_6_초각스", 6)]
    [InlineData("lt_3_초각스", 3)]
    [InlineData("lt_1_일반1", 1)]
    public void A_sweep_edge_is_not_a_tens_one(string name, int shown)
    {
        var f = Fixture.Load(name);
        Assert.Equal((ReadKind.Seconds, shown), new DigitReader(1).Read(f.Frames[0], Enum.Parse<SlotId>(f.Slot)));
    }

    // gameplay.mp4, HUD 100, just after S is cast: the white sweep line at 12 o'clock matched a tens '1' overlapping the
    // 8, so "8s" read 18. A real tens digit never overlaps the ones digit.
    [Fact]
    public void A_one_overlapping_the_ones_digit_is_not_a_tens()
    {
        var f = Fixture.Load("gameplay_8_일반6");
        Assert.Equal((ReadKind.Seconds, 8), new DigitReader(Geometry.Scale(Fhd, new Fit(100))).Read(f.Frames[0], SlotId.일반6));
    }

    // Q with both uses shows the count "2", whose '2' is shaped differently from the timer's. The template is cut from
    // live_test2; live test is a separate recording.
    [Theory]
    [InlineData("lt2_count2_일반1")]
    [InlineData("lt_count2_일반1")]
    public void Sees_a_count_of_two(string name) => Assert.True(new DigitReader(1).ShowsCount(Fixture.Load(name).Frames[0]));

    [Fact]
    public void Reads_the_gameplay_D_countdown_at_hud_100()
    {
        var f = Fixture.Load("gameplay_D_end");
        var digits = new DigitReader(Geometry.Scale(Fhd, new Fit(100)));
        Assert.Equal((ReadKind.Seconds, 2), digits.Read(f.Frames[0], SlotId.일반7));
        Assert.Equal((ReadKind.Seconds, 2), digits.Read(f.Frames[20], SlotId.일반7));
        Assert.Equal((ReadKind.Seconds, 1), digits.Read(f.Frames[60], SlotId.일반7));
        Assert.Equal(ReadKind.Ready, digits.Read(f.Frames[105], SlotId.일반7).Kind);
    }

    /// <summary>Rewrites Core/Vision/DigitTemplates.cs from <see cref="Sources"/>. Runs only with
    /// GAUGE_BUILD_TEMPLATES=1, because it edits the source tree.</summary>
    [Fact]
    public void Build_templates_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TEMPLATES") != "1") return;

        var sb = new StringBuilder();
        sb.AppendLine("namespace GaugeOverlay.Core.Vision;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Digit glyphs at reference scale (HUD 110, 1080p), averaged from real crops. Generated by");
        sb.AppendLine("/// DigitReaderTests.Build_templates_when_requested (GAUGE_BUILD_TEMPLATES=1); do not edit by hand.");
        sb.AppendLine("/// One string per row; \" .:-=+*#%@\" is grey (min of R,G,B) 0..255 in 10 steps.</summary>");
        sb.AppendLine("internal static class DigitTemplates");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The cooldown timer's glyphs.</summary>");
        Emit(sb, "Main", Samples(Sources, 0.25, 0.66, 0.1, 0.85), "0123456789sm");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Stack-count glyphs the shrunk timer glyphs don't match (the count font draws its '2' differently).</summary>");
        Emit(sb, "Count", Samples(CountSources, 0.55, 0.98, 0.62, 0.98), "2");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(RepoRoot(), "Core", "Vision", "DigitTemplates.cs"), sb.ToString().ReplaceLineEndings("\r\n"));
    }

    /// <summary>Glyph crops per character from <paramref name="sources"/>, segmented inside the given share of the crop.</summary>
    private static Dictionary<char, List<(int W, int H, float[] Grey)>> Samples(
        (string Fixture, int Frame, string Text)[] sources, double yTop, double yBot, double xLo, double xHi)
    {
        var samples = new Dictionary<char, List<(int W, int H, float[] Grey)>>();
        foreach (var (name, frame, text) in sources)
        {
            var img = Fixture.Load(name).Frames[frame];
            var boxes = Segment(img, yTop, yBot, xLo, xHi);
            if (text.EndsWith('m') && boxes.Count == text.Length + 1)   // the thin middle stem of 'm' splits it
            {
                var (a, b) = (boxes[^2], boxes[^1]);
                boxes[^2] = (a.X0, Math.Min(a.Y0, b.Y0), b.X1, Math.Max(a.Y1, b.Y1));
                boxes.RemoveAt(boxes.Count - 1);
            }
            if (boxes.Count != text.Length) throw new Exception($"{name}#{frame}: {boxes.Count} segments for \"{text}\": {string.Join(" ", boxes)}");
            for (int i = 0; i < text.Length; i++)
            {
                var (x0, y0, x1, y1) = boxes[i];
                // 1 px margin keeps the dark outline in the template.
                x0--; y0--; x1++; y1++;
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                var g = new float[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) g[y * w + x] = Grey(img, x0 + x, y0 + y);
                (samples.TryGetValue(text[i], out var l) ? l : samples[text[i]] = []).Add((w, h, g));
            }
        }
        return samples;
    }

    /// <summary>Appends one template array: per character, the samples averaged at their most common size.</summary>
    private static void Emit(StringBuilder sb, string name, Dictionary<char, List<(int W, int H, float[] Grey)>> samples, string chars)
    {
        sb.AppendLine($"    public static readonly (char Ch, string[] Rows)[] {name} =");
        sb.AppendLine("    [");
        foreach (char ch in chars)
        {
            var list = samples[ch];
            // Most common size wins; other samples are resampled to it.
            var (w, h) = list.GroupBy(s => (s.W, s.H)).OrderByDescending(g => g.Count()).First().Key;
            var avg = new float[w * h];
            foreach (var s in list)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        avg[y * w + x] += DigitReader.Glyph.Bilinear(s.Grey, s.W, s.H, (x + 0.5) * s.W / w - 0.5, (y + 0.5) * s.H / h - 0.5) / list.Count;
            sb.AppendLine($"        ('{ch}', [ // {list.Count} sample(s)");
            for (int y = 0; y < h; y++)
            {
                var row = new char[w];
                for (int x = 0; x < w; x++) row[x] = DigitReader.GreyRamp[Math.Clamp((int)(avg[y * w + x] / 25.6f), 0, 9)];
                sb.AppendLine($"            \"{new string(row)}\",");
            }
            sb.AppendLine("        ]),");
        }
        sb.AppendLine("    ];");
    }

    private static float Grey(Image img, int x, int y)
    {
        var (b, g, r) = img.Pixel(Math.Clamp(x, 0, img.Width - 1), Math.Clamp(y, 0, img.Height - 1));
        return Math.Min(b, Math.Min(g, r));
    }

    /// <summary>Glyph boxes (inclusive) of white, low-chroma pixels in the band between <paramref name="top"/> and
    /// <paramref name="bottom"/> (shares of the height), between <paramref name="left"/> and <paramref name="right"/>
    /// (shares of the width), split by empty columns.</summary>
    private static List<(int X0, int Y0, int X1, int Y1)> Segment(Image img, double top, double bottom, double left, double right)
    {
        int yTop = (int)(img.Height * top), yBot = (int)(img.Height * bottom);
        bool Core(int x, int y)
        {
            var (b, g, r) = img.Pixel(x, y);
            int mn = Math.Min(b, Math.Min(g, r)), mx = Math.Max(b, Math.Max(g, r));
            return mn >= 180 && mx - mn <= 50;
        }
        var boxes = new List<(int, int, int, int)>();
        int start = -1;
        int xEnd = Math.Min(img.Width - 1, (int)(img.Width * right));
        for (int x = (int)(img.Width * left); x <= xEnd; x++)
        {
            bool any = false;
            for (int y = yTop; y < yBot && !any; y++) any = Core(x, y);
            if (any && start < 0) start = x;
            if ((!any || x == xEnd) && start >= 0)
            {
                int x1 = any ? x : x - 1, y0 = int.MaxValue, y1 = -1;
                for (int xx = start; xx <= x1; xx++)
                    for (int y = yTop; y < yBot; y++)
                        if (Core(xx, y)) { y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
                if (y1 - y0 >= 5) boxes.Add((start, y0, x1, y1));
                start = -1;
            }
        }
        return boxes;
    }
}
