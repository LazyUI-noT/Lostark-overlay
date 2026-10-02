using System.Text;
using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using static GaugeOverlay.Tests.Helpers;

namespace GaugeOverlay.Tests;

public class DebuffTests
{
    // mordum.png and its crops (tools/make-fixtures.ps1): 1920×1080, HUD 110, buff 120%; every debuffrow fixture shows
    // screen (1060, 880, 200, 60), bossrow screen (700, 140, 540, 70).
    static readonly Fit Hud110 = new(110);
    const int Buff = 120;
    static readonly PixelRect RowRect = new(1060, 880, 200, 60);

    static Geometry.BuffBox[] Boxes() =>
        [.. Geometry.DebuffBoxes(Fhd, Hud110, Buff).Select(b => new Geometry.BuffBox(
            b.Icon with { X = b.Icon.X - RowRect.X, Y = b.Icon.Y - RowRect.Y },
            b.Timer with { X = b.Timer.X - RowRect.X, Y = b.Timer.Y - RowRect.Y }))];

    // Box 0 first. B 파랑, R 빨강, L 번개, S 교체, '-': another debuff or no icon. The timers of the labelled boxes in the
    // same order (null: none).
    static readonly string[] Labels = ["B----", "BL---", "BS---", "--RL-", "BS---", "R----"];
    static readonly double?[][] Timers = [[null], [null, 6], [null, 3], [null, 6], [null, 0.4], [null]];

    [Fact]
    public void The_debuff_row_mirrors_the_buff_row_about_the_hud_centre()
    {
        var debuffs = Geometry.DebuffBoxes(Fhd, Hud110, Buff);
        var buffs = Geometry.BuffBoxes(Fhd, Hud110, Buff);
        // mordum.png: the first frame spans 1074..1106, the next starts 35 px on.
        Assert.InRange(debuffs[0].Icon.X, 1074, 1076);
        Assert.InRange(debuffs[1].Icon.X - debuffs[0].Icon.X, 34, 36);
        Assert.Equal(buffs[0].Icon.Y, debuffs[0].Icon.Y);
        Assert.InRange(debuffs[0].Icon.Right + buffs[0].Icon.X, 1919, 1921);
    }

    [Fact]
    public void Each_labelled_box_reads_as_its_debuff_and_nothing_else_reads_at_all()
    {
        var boxes = Boxes();
        for (int f = 0; f < Labels.Length; f++)
        {
            var img = Fixture.Load($"debuffrow_{f}").Frames[0];
            for (int b = 0; b < boxes.Length; b++)
            {
                DebuffKind? want = Labels[f][b] switch
                { 'B' => DebuffKind.파랑, 'R' => DebuffKind.빨강, 'L' => DebuffKind.번개, 'S' => DebuffKind.교체, _ => null };
                Assert.True(want == DebuffReader.Classify(img, boxes[b].Icon), $"debuffrow_{f} box {b}: want {want}");
            }
        }
    }

    [Fact]
    public void The_lightning_and_swap_timers_read_in_the_buff_font()
    {
        var reader = new BuffReader(Geometry.BuffScale(Fhd, Buff));
        var boxes = Boxes();
        for (int f = 0; f < Labels.Length; f++)
        {
            var img = Fixture.Load($"debuffrow_{f}").Frames[0];
            var read = Enumerable.Range(0, boxes.Length).Where(b => Labels[f][b] != '-')
                .Select(b => Labels[f][b] is 'L' or 'S' ? reader.ReadTimer(img, boxes[b].Timer) : null);
            Assert.Equal(Timers[f], read);
        }
    }

    [Theory]
    [InlineData("bossrow", 4, 4)]
    [InlineData("bossrow_53", 5, 3)]   // 18-22.mp4 21:05
    [InlineData("bossrow_62", 6, 2)]   // 18-22.mp4 21:37.5
    [InlineData("bossrow_62b", 6, 2)]  // 21:42.5
    [InlineData("bossrow_61", 6, 1)]   // 21:50: one stack shows no "X n", as a buff's doesn't
    public void The_boss_counts_read_as_shown(string fixture, int red, int blue)
    {
        var row = Fixture.Load(fixture).Frames[0];
        Assert.Equal((red, blue), DebuffReader.BossCounts(row, 1));
    }

    [Fact]
    public void A_row_without_the_boss_sigils_reads_no_count()
    {
        // Your own debuff row: the sigils are there, but no "X n" over them.
        var row = Fixture.Load("debuffrow_3").Frames[0];
        Assert.Equal((null, null), DebuffReader.BossCounts(row, 1));
    }

    [Fact]
    public void No_known_digit_passes_as_another()
    {
        foreach (var (fixture, cell, digit) in Glyphs)
        {
            var own = BuffReader.Luma(Fixture.Load(fixture).Frames[0], cell);
            foreach (var (f2, c2, d2) in Glyphs.Where(g => g.Digit != digit))
            {
                double s = BuffReader.Correlate(own, BuffReader.Luma(Fixture.Load(f2).Frames[0], c2));
                Assert.True(s < DebuffReader.MinCount, $"{digit} against {d2}: {s:0.00}");
            }
        }
    }

    // ── Swap advice and tracking ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DebuffKind.빨강, 6, 2, DebuffKind.빨강, 1)]   // the user's example: red, boss 6 red 2 blue → bring 1 red
    [InlineData(DebuffKind.빨강, 5, 3, DebuffKind.빨강, 0)]   // go alone
    [InlineData(DebuffKind.빨강, 4, 4, DebuffKind.파랑, 1)]
    [InlineData(DebuffKind.파랑, 6, 2, DebuffKind.빨강, 3)]
    [InlineData(DebuffKind.파랑, 3, 5, DebuffKind.파랑, 0)]
    public void The_advice_evens_the_boss_counts(DebuffKind mine, int red, int blue, DebuffKind bring, int count)
    {
        Assert.Equal((bring, count), DebuffAdvice.Bring(mine, red, blue));
        // You and everyone brought change colour: the counts end up even.
        int r = red + (mine == DebuffKind.빨강 ? -1 : 1) + (bring == DebuffKind.빨강 ? -count : count);
        Assert.Equal((red + blue) / 2, r);
    }

    [Fact]
    public void No_advice_without_both_counts_or_a_colour()
    {
        Assert.Null(DebuffAdvice.Bring(DebuffKind.빨강, null, 4));
        Assert.Null(DebuffAdvice.Bring(DebuffKind.번개, 4, 4));
    }

    static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);
    static TimeSpan At(double s) => T0 + TimeSpan.FromSeconds(s);

    [Fact]
    public void A_timer_counts_down_as_the_game_shows_it()
    {
        var s = DebuffTracker.Update(DebuffStatus.None, [new(DebuffKind.번개, 6)], null, null, T0);
        Assert.Equal(6, s.Lightning!.Seconds(T0));
        Assert.Equal(5, s.Lightning.Seconds(At(1.5)));
    }

    [Fact]
    public void A_debuff_missed_for_one_read_stays_and_goes_after_half_a_second()
    {
        var s = DebuffTracker.Update(DebuffStatus.None, [new(DebuffKind.번개, 6)], null, null, T0);
        s = DebuffTracker.Update(s, [], null, null, At(0.1));
        Assert.NotNull(s.Lightning);
        s = DebuffTracker.Update(s, [], null, null, At(0.6));
        Assert.Null(s.Lightning);
    }

    [Fact]
    public void A_sigil_just_seen_replaces_the_other_at_once()
    {
        var s = DebuffTracker.Update(DebuffStatus.None, [new(DebuffKind.빨강, null)], null, null, T0);
        Assert.Equal(DebuffKind.빨강, s.Colour);
        s = DebuffTracker.Update(s, [new(DebuffKind.파랑, null)], null, null, At(0.1));
        Assert.Equal(DebuffKind.파랑, s.Colour);
        Assert.Null(s.Red);
    }

    [Fact]
    public void A_hidden_hud_freezes_everything_and_the_holds_start_over_when_it_is_back()
    {
        var s = DebuffTracker.Update(DebuffStatus.None, [new(DebuffKind.빨강, null)], 6, 2, T0);
        s = DebuffTracker.Update(s, [], null, null, At(5), hudVisible: false);   // a loading screen
        s = DebuffTracker.Update(s, [], null, null, At(5.1));
        Assert.Equal(DebuffKind.빨강, s.Colour);
        Assert.Equal((6, 2), (s.BossRed, s.BossBlue));
    }

    [Fact]
    public void A_count_missed_keeps_its_last_value_while_the_other_is_unchanged()
    {
        // 18-22.mp4 21:45: "X 5" / "X 2", 7 in all (a raider down); the 5 missed under a spell effect stays 5, not 8 − 2.
        var s = DebuffTracker.Update(DebuffStatus.None, [], 5, 2, T0);
        s = DebuffTracker.Update(s, [], null, 2, At(0.5));
        Assert.Equal((5, 2), (s.BossRed, s.BossBlue));
    }

    [Fact]
    public void A_count_read_alone_completes_the_other_and_both_hold_through_a_miss()
    {
        var s = DebuffTracker.Update(DebuffStatus.None, [], 6, null, T0);
        Assert.Equal((6, 2), (s.BossRed, s.BossBlue));
        s = DebuffTracker.Update(s, [], null, null, At(2));
        Assert.Equal((6, 2), (s.BossRed, s.BossBlue));
        s = DebuffTracker.Update(s, [], null, null, At(4));
        Assert.Equal((null, null), (s.BossRed, s.BossBlue));
    }

    // ── Templates ─────────────────────────────────────────────────────────────────────────────────────────────────

    // Count glyph cells (8×11): buff stacks "X 10", "X 6" (stacks_1), "X 8" (stacks_2) and the boss's "X 4", "X 5",
    // "X 3" and "X 2" (each cell 15 px right of its sigil's left edge). 7 has no sample yet.
    static readonly (string Fixture, PixelRect Cell, char Digit)[] Glyphs =
    [
        ("stacks_1", new(33, 7, 8, 11), '1'), ("stacks_1", new(42, 7, 8, 11), '0'), ("stacks_1", new(179, 7, 8, 11), '6'),
        ("stacks_2", new(56, 9, 8, 11), '8'), ("bossrow", new(163, 15, 8, 11), '4'),
        ("bossrow_53", new(163, 15, 8, 11), '5'), ("bossrow_53", new(197, 15, 8, 11), '3'), ("bossrow_62", new(197, 15, 8, 11), '2'),
        ("bossrow_62b", new(197, 15, 8, 11), '2'),   // a 2 over a brighter background
    ];

    // The "X" of the boss's red "X 4" in bossrow.
    static readonly PixelRect XCell = new(150, 15, 10, 11);

    [Fact]
    public void Build_debuff_templates_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TEMPLATES") != "1") return;
        var boxes = Boxes();
        var cuts = new (DebuffKind Kind, int Fixture, int Box)[] { (DebuffKind.파랑, 0, 0), (DebuffKind.빨강, 3, 2), (DebuffKind.번개, 1, 1), (DebuffKind.교체, 2, 1) };
        var sb = new StringBuilder();
        sb.AppendLine("namespace GaugeOverlay.Core.Vision;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>모르둠's four debuff icons (DebuffKind order), frame left out (BuffReader.Interior), cut at buff 120% from the");
        sb.AppendLine("/// mordum screenshots, and the count digits known so far (8×11 at 1080p). Image.ToBytes as base64. Generated by");
        sb.AppendLine("/// DebuffTests.Build_debuff_templates_when_requested (GAUGE_BUILD_TEMPLATES=1); do not edit by hand.</summary>");
        sb.AppendLine("internal static class DebuffTemplates");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly string[] Icons =");
        sb.AppendLine("    [");
        foreach (var c in cuts.OrderBy(c => c.Kind))
        {
            var img = Fixture.Load($"debuffrow_{c.Fixture}").Frames[0];
            sb.AppendLine($"        \"{Convert.ToBase64String(BuffReaderTests.Crop(img, BuffReader.Interior(boxes[c.Box].Icon)).ToBytes())}\",   // {c.Kind}");
        }
        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    public static readonly (char Digit, string Glyph)[] Digits =");
        sb.AppendLine("    [");
        foreach (var (fixture, cell, digit) in Glyphs.OrderBy(g => g.Digit))
            sb.AppendLine($"        ('{digit}', \"{Convert.ToBase64String(BuffReaderTests.Crop(Fixture.Load(fixture).Frames[0], cell).ToBytes())}\"),");
        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The \"X\" before a count (10×11), 13 px left of its digit: none over a sigil is one stack.</summary>");
        sb.AppendLine($"    public static readonly string CountX = \"{Convert.ToBase64String(BuffReaderTests.Crop(Fixture.Load("bossrow").Frames[0], XCell).ToBytes())}\";");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(RepoRoot(), "Core", "Vision", "DebuffTemplates.cs"), sb.ToString().ReplaceLineEndings("\r\n"));
    }

    /// <summary>The debuff window's 내 색 box shows your sigil as the game draws it: the template the reader finds it by.</summary>
    [Fact]
    public void The_sigil_icons_shown_are_blue_and_red()
    {
        static (double B, double R) Mean(Image i) => (Enumerable.Range(0, i.Width * i.Height).Average(p => i.Bgra[p * 4]),
            Enumerable.Range(0, i.Width * i.Height).Average(p => i.Bgra[p * 4 + 2]));
        var (b, r) = Mean(DebuffReader.Icon(DebuffKind.파랑));
        Assert.True(b > r * 1.5, $"파랑 B {b:0} R {r:0}");
        (b, r) = Mean(DebuffReader.Icon(DebuffKind.빨강));
        Assert.True(r > b * 1.5, $"빨강 B {b:0} R {r:0}");
    }
}
