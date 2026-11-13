using System.Text;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class BuffReaderTests
{
    // buff_gameplay.mp4 (HUD 110, buff 120%) at 5 fps, fixture origin (664, 886); 4k_down.mp4 (viewport 0,135,1920,810,
    // HUD 100, buff 100%), origin (760, 815).
    internal static readonly (string Fixture, Viewport Vp, Fit Fit, int Buff, PixelRect Rect) Bg =
        ("buffrow_bg", Fhd, new Fit(110), 120, new(664, 886, 184, 60));
    internal static readonly (string Fixture, Viewport Vp, Fit Fit, int Buff, PixelRect Rect) Down =
        ("buffrow_down", new Viewport(0, 135, 1920, 810), new Fit(100), 100, new(760, 815, 124, 40));

    [Theory]
    // HUD80_buff120.png / HUD90_buff120.png: the four party buffs sit in boxes 0–3 of a row packed tighter than at HUD 100+.
    [InlineData("buffrow_hud80", 80, 630, 925, 250, 60)]
    [InlineData("buffrow_hud90", 90, 610, 912, 260, 60)]
    public void The_four_party_buffs_are_found_in_a_small_hud_row(string fixture, int hud, int x, int y, int w, int h)
    {
        var src = (fixture, Fhd, new Fit(hud), 120, new PixelRect(x, y, w, h));
        var frame = Fixture.Load(fixture).Frames[0];
        var kinds = Boxes(src).Take(4).Select(b => BuffReader.Classify(frame, b.Icon)).ToArray();
        Assert.All(kinds, k => Assert.NotNull(k));
        Assert.Equal(4, kinds.Distinct().Count());
    }

    /// <summary>The boxes relative to a fixture's crop.</summary>
    internal static Geometry.BuffBox[] Boxes((string Fixture, Viewport Vp, Fit Fit, int Buff, PixelRect Rect) src) =>
        [.. Geometry.BuffBoxes(src.Vp, src.Fit, src.Buff).Select(b => new Geometry.BuffBox(
            b.Icon with { X = b.Icon.X - src.Rect.X, Y = b.Icon.Y - src.Rect.Y },
            b.Timer with { X = b.Timer.X - src.Rect.X, Y = b.Timer.Y - src.Rect.Y }))];

    internal static Image Crop(Image img, PixelRect r)
    {
        var dst = new Image(r.Width, r.Height);
        for (int y = 0; y < r.Height; y++) img.Row(r.Y + y).Slice(r.X * 4, r.Width * 4).CopyTo(dst.Bgra.AsSpan(y * dst.Stride));
        return dst;
    }


    // Ground truth, box 0 (rightmost) first. A 공증, D 아덴피증, C 초각스피증, M 뎀증; lowercase: the same buff under the
    // glare the game sweeps over a newly applied icon (may be missed, never misread); '-': another buff or no icon.
    // buff_gameplay, one frame a second (fixture frame = 5 × second), read off the recording.
    internal static readonly string[] BgLabels =
    [
        "-----", "-----", "-----", "A----", "A----", "ACM--", "ACM--", "ACMD-", "ACMd-", "CMDa-",
        "CMDA-", "CMdA-", "CMDa-", "CMDA-", "CMdA-", "MDAa-", "MDAA-", "MDA--", "MDA--", "MAA--",
        "MaA--", "MAAC-", "MAACD", "MaaCd", "MAACD", "MAACD", "MaCd-", "MCD--", "MCD--", "MCd--",
    ];
    // The timers shown on the labelled buffs, in label order (seconds; "0.7초" = 0.7).
    internal static readonly double[][] BgTimers =
    [
        [], [], [], [7], [6], [5, 9, 19], [4, 8, 18], [3, 7, 17, 11], [2, 6, 16, 10], [5, 15, 9, 7],
        [4, 14, 8, 6], [3, 13, 7, 5], [2, 12, 6, 4], [1, 11, 5, 3], [0.0, 10, 4, 2], [9, 3, 1, 7], [8, 2, 0.8, 6], [7, 1, 5], [6, 0.7, 7], [5, 6, 7],
        [4, 5, 6], [19, 4, 5, 9], [18, 3, 4, 8, 11], [17, 2, 3, 7, 10], [16, 1, 2, 6, 9], [15, 0.2, 1, 5, 8], [14, 0.3, 4, 7], [13, 3, 11], [12, 2, 10], [11, 1, 9],
    ];
    // 4k_down, one frame every 2 s (fixture frame = 10 × index). Look-alikes here: 바드's pink rose, purple flower,
    // green arc and blue swirl.
    internal static readonly string[] DownLabels =
    [
        "D----", "D----", "D-A--", "d-a--", "D-A--", "D-ACM", "d-CM-", "D-CM-", "-CM-a", "CM-A-", "CM-A-", "CM-A-",
        "M-AD-", "M-D-A", "M-D-A", "M-D-A", "M-D-A", "M-D-A", "M-D-A", "M-D-A", "--A--", "-A---", "-----",
    ];
    // buff_wrong_detect.mp4 (발키리 해방자, set up as buff_gameplay) at 5 fps, one frame a second. From 2.6 s 발키리's own
    // gold dome buff (brown frame) sits in box 1, then box 2; the real 아덴피증 arrives in box 0 at 6.4 s.
    internal static readonly string[] WrongLabels =
        ["-----", "-----", "-----", "-----", "-----", "-----", "-----", "D----", "D----", "D----", "D----"];

    internal static BuffKind? Label(char c) => char.ToUpperInvariant(c) switch
    {
        'A' => BuffKind.공증, 'D' => BuffKind.아덴피증, 'C' => BuffKind.초각스피증, 'M' => BuffKind.뎀증, _ => null,
    };

    [Theory]
    [InlineData("buffrow_bg")]
    [InlineData("buffrow_down")]
    [InlineData("buffrow_wrong")]
    public void Finds_the_four_buffs_and_rejects_look_alikes(string fixture)
    {
        var (src, labels, step) = fixture switch
        {
            "buffrow_bg" => (Bg, BgLabels, 5),
            "buffrow_down" => (Down, DownLabels, 10),
            _ => (Bg with { Fixture = fixture }, WrongLabels, 5),
        };
        var f = Fixture.Load(src.Fixture);
        var boxes = Boxes(src);
        for (int i = 0; i < labels.Length; i++)
            for (int b = 0; b < labels[i].Length; b++)   // the recordings label the first five boxes
            {
                char want = labels[i][b];
                var got = BuffReader.Classify(f.Frames[i * step], boxes[b].Icon);
                string at = $"{fixture} frame {i * step} box {b} (label {want})";
                if (char.IsUpper(want)) Assert.True(got == Label(want), $"{at}: got {got}");
                else if (char.IsLower(want)) Assert.True(got is null || got == Label(want), $"{at}: misread as {got}");
                else Assert.True(got is null, $"{at}: false {got}");
            }
    }
    // v1_1막_clear.mp4 (solo, so no party buff is up), HUD 110, buff 120%: the whole 14-box row at seven moments the reader
    // once found 공증 or 뎀증 in an empty box over the scenery (structure 0.60–0.69, no frame), one of them a flash frame.
    internal static readonly (string Fixture, Viewport Vp, Fit Fit, int Buff, PixelRect Rect) V1Empty =
        ("buffrow_v1empty", Fhd, new Fit(110), 120, new(354, 892, 493, 50));

    /// <summary>The row as the engine reads it: <see cref="BuffReader.ReadRow"/> over the boxes inside the crop.</summary>
    static BuffKind?[] Row(Image frame, (string Fixture, Viewport Vp, Fit Fit, int Buff, PixelRect Rect) src, int count = Geometry.MaxBuffBoxCount)
    {
        var boxes = Geometry.BuffBoxes(src.Vp, src.Fit, src.Buff, count)
            .Select(b => b.Icon with { X = b.Icon.X - src.Rect.X, Y = b.Icon.Y - src.Rect.Y })
            .TakeWhile(r => r.X >= 0 && r.Right <= frame.Width).ToArray();
        return BuffReader.ReadRow(boxes.Length, i => BuffReader.Framed(frame, boxes[i]), i => BuffReader.Read(frame, boxes[i]));
    }

    [Fact]
    public void Empty_boxes_over_the_scenery_read_no_buff()
    {
        var f = Fixture.Load(V1Empty.Fixture);
        var icons = Geometry.BuffBoxes(V1Empty.Vp, V1Empty.Fit, V1Empty.Buff, Geometry.MaxBuffBoxCount)
            .Select(b => b.Icon with { X = b.Icon.X - V1Empty.Rect.X, Y = b.Icon.Y - V1Empty.Rect.Y }).ToArray();
        for (int i = 0; i < f.Frames.Count; i++)
        {
            var frame = f.Frames[i];
            // the old per-box read finds one here
            Assert.True(icons.Any(r => BuffReader.Classify(frame, r) is not null), $"frame {i} ({f.Pts[i].TotalSeconds} s): no false read to guard");
            var row = Row(frame, V1Empty);
            Assert.True(row.All(k => k is null), $"frame {i} ({f.Pts[i].TotalSeconds} s): {string.Join(",", row)}");
        }
    }

    [Theory]
    [InlineData("buffrow_bg")]
    [InlineData("buffrow_down")]
    [InlineData("buffrow_wrong")]
    public void The_row_read_keeps_every_labelled_buff(string fixture)
    {
        var (src, labels, step) = fixture switch
        {
            "buffrow_bg" => (Bg, BgLabels, 5),
            "buffrow_down" => (Down, DownLabels, 10),
            _ => (Bg with { Fixture = fixture }, WrongLabels, 5),
        };
        var f = Fixture.Load(src.Fixture);
        for (int i = 0; i < labels.Length; i++)
        {
            var row = Row(f.Frames[i * step], src);
            for (int b = 0; b < labels[i].Length; b++)
                if (char.IsUpper(labels[i][b]))
                    Assert.True(row[b] == Label(labels[i][b]), $"{fixture} frame {i * step} box {b} (label {labels[i][b]}): got {row[b]}");
        }
    }

    [Theory]
    [InlineData(0.62, true, true)]    // inside the framed run: the usual bar
    [InlineData(0.69, false, false)]  // past it: an empty box's best (v1) is not enough
    [InlineData(0.80, false, true)]   // past it: a frameless real icon (another player's 뎀증) still reads
    public void A_box_past_the_framed_run_needs_a_closer_match(double structure, bool framed, bool kept)
    {
        var row = BuffReader.ReadRow(1, _ => framed, _ => (BuffKind.공증, structure));
        Assert.Equal(kept ? BuffKind.공증 : null, row[0]);
    }

    [Fact]
    public void An_unframed_box_between_framed_ones_is_read_at_the_usual_bar()
    {
        bool[] framed = [true, false, true, false, false];
        var row = BuffReader.ReadRow(5, i => framed[i], i => (BuffKind.뎀증, 0.62));
        Assert.Equal([BuffKind.뎀증, BuffKind.뎀증, BuffKind.뎀증, null, null], row);
    }

    /// <summary>F framed, - not; every box reads 뎀증 at 0.62 (passes the usual bar, not the past-the-run one).
    /// K kept, . dropped. The walk stops at two unframed boxes in a row, so a framed box after them is past the run.</summary>
    [Theory]
    [InlineData("", "")]           // no boxes
    [InlineData("---", "...")]     // nothing framed: the whole row needs the closer match
    [InlineData("FFF", "KKK")]     // all framed: the usual bar everywhere
    [InlineData("-F", "KK")]       // one gap at the start is passed
    [InlineData("--F", "...")]     // two gaps end the walk before the framed box
    [InlineData("F--F", "K...")]   // same in the middle: the later framed box is not credited
    public void The_framed_run_decides_which_boxes_read_at_the_usual_bar(string framed, string kept)
    {
        var row = BuffReader.ReadRow(framed.Length, i => framed[i] == 'F', _ => (BuffKind.뎀증, 0.62));
        Assert.Equal(kept.Select(c => c == 'K' ? BuffKind.뎀증 : (BuffKind?)null), row);
    }

    /// <summary>Rewrites Core/Vision/BuffTemplates.cs from buff_gameplay frames where each icon is clean (no glare):
    /// 5 s (공증 box 0, 초각스피증 box 1, 뎀증 box 2) and 7 s (아덴피증 box 3), and the look-alikes from buff_wrong_detect. Runs only with GAUGE_BUILD_TEMPLATES=1.</summary>
    [Fact]
    public void Build_buff_templates_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TEMPLATES") != "1") return;
        var f = Fixture.Load(Bg.Fixture);
        var boxes = Boxes(Bg);
        // DX, DY: where each icon was cut relative to today's box (the box geometry has since moved by a pixel), so a
        // rebuild keeps the shipped icons.
        var cuts = new (BuffKind Kind, int Frame, int Box, int DX, int DY)[]
            { (BuffKind.공증, 25, 0, 0, 1), (BuffKind.아덴피증, 35, 3, 1, 1), (BuffKind.초각스피증, 25, 1, 0, 1), (BuffKind.뎀증, 25, 2, 1, 1) };
        var sb = new StringBuilder();
        sb.AppendLine("using GaugeOverlay.Core.Domain;");
        sb.AppendLine();
        sb.AppendLine("namespace GaugeOverlay.Core.Vision;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The four buff icons (BuffKind order), gold frame left out (BuffReader.Interior), cut at buff 120% from buff_gameplay.mp4.");
        sb.AppendLine("/// Image.ToBytes as base64. Generated by BuffReaderTests.Build_buff_templates_when_requested (GAUGE_BUILD_TEMPLATES=1); do not edit by hand.</summary>");
        sb.AppendLine("internal static class BuffTemplates");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly string[] Icons =");
        sb.AppendLine("    [");
        foreach (var c in cuts.OrderBy(c => c.Kind))
        {
            var r = BuffReader.Interior(boxes[c.Box].Icon);
            sb.AppendLine($"        \"{Convert.ToBase64String(Crop(f.Frames[c.Frame], r with { X = r.X + c.DX, Y = r.Y + c.DY }).ToBytes())}\",   // {c.Kind}");
        }
        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Other icons close enough to a buff to pass its checks; a box that matches one of these at least as well as");
        sb.AppendLine("    /// the buff it resembles is no buff. Cut the same way.</summary>");
        sb.AppendLine("    public static readonly (BuffKind Of, string Icon)[] LookAlikes =");
        sb.AppendLine("    [");
        var dome = Fixture.Load("buffrow_wrong").Frames[25];
        sb.AppendLine($"        (BuffKind.아덴피증, \"{Convert.ToBase64String(Crop(dome, BuffReader.Interior(boxes[1].Icon)).ToBytes())}\"),   // 발키리's own gold dome (buff_wrong_detect.mp4, 5 s, box 1), 0.63");
        sb.AppendLine("    ];");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(RepoRoot(), "Core", "Vision", "BuffTemplates.cs"), sb.ToString().ReplaceLineEndings("\r\n"));
    }
}
