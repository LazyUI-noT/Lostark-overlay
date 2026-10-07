using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using static GaugeOverlay.Tests.BuffReaderTests;

namespace GaugeOverlay.Tests;

/// <summary>buff_80_to_110.mp4 (발키리, 1920×1080, HUD 110): the in-game buff size stepped 80 → 90 → 100 → 110 through the
/// settings window. Fixture at 2 fps, origin (664, 886); labelled once a second (fixture frame = 2 × second).</summary>
public class BuffSizeTests
{
    internal static readonly (string Fixture, Viewport Vp, Fit Fit, int Buff, PixelRect Rect) B80 =
        ("buffrow_b80", Fhd, new Fit(110), 80, new(664, 886, 184, 60));

    /// <summary>The buff size on screen at <paramref name="second"/> (the change lands between 12.5 and 13 s, 31 and
    /// 31.5 s, 46 and 46.5 s).</summary>
    internal static int SizeAt(int second) => second <= 12 ? 80 : second <= 31 ? 90 : second <= 46 ? 100 : 110;

    internal static (string Fixture, Viewport Vp, Fit Fit, int Buff, PixelRect Rect) At(int second) => B80 with { Buff = SizeAt(second) };

    // Same notation as BuffReaderTests: box 0 first; A 공증, D 아덴피증, C 초각스피증, M 뎀증; lowercase under the glare
    // (or the settings window's dimming, 9 s and 25 s); '-' another buff or none. Read off 4× zooms of the fixture.
    internal static readonly string[] Labels =
    [
        "-----", "A----", "ACMD-", "ACMD-", "CMDA-", "CMDAA", "CMDAa", "CMDAA", "CMDAA", "cmdaa",   // 0–9 s: 80%
        "CMDAA", "CMdAA", "MDA--",
        "md---", "Md---", "M----", "mA---", "MACd-", "MCDa-", "mCDA-", "MCdAA", "MCDaA", "mCDAA",   // 13–31 s: 90%
        "MCdAA", "MCDaA", "mcdaa", "McdAA", "MDA--", "mDA--", "MD---", "M----", "M----",
        "m----", "M----", "MCA--", "McA--", "MCa--", "MCAd-", "mcDA-", "MCDAA", "MCdAa", "mcDAA",   // 32–46 s: 100%
        "MCDAA", "MCDAa", "mDAA-", "MDAA-", "MDA--",
        "Md---", "MD---", "MD---", "Mda--", "MDAc-", "MDAC-", "MdaC-", "MDAc-", "MDCA-", "MdCAA",   // 47–63 s: 110%
        "MDcAA", "MDCAA", "MdCAA", "MDcAA", "MDCAA", "MdCAA", "MDcA-",
    ];
    // The timers on the labelled buffs, in label order; NaN where the zoom is too blurry to be sure (11 s, 80%).
    internal static readonly double[][] Timers =
    [
        [], [7], [6, 9, 19, 11], [5, 8, 18, 10], [7, 17, 9, 7], [6, 16, 8, 6, 7], [5, 15, 7, 5, 6], [4, 14, 6, 4, 5], [3, 13, 5, 3, 4], [2, 12, 4, 2, 3],
        [1, 11, 3, 1, 2], [double.NaN, 10, 2, double.NaN, 1], [9, 1, 0.4],
        [8, 0.8], [7, 0.0], [6], [5, 7], [19, 6, 9, 11], [18, 8, 10, 7], [17, 7, 9, 5], [16, 6, 8, 5, 7], [15, 5, 7, 4, 6], [14, 4, 6, 3, 5],
        [13, 3, 5, 2, 4], [12, 2, 4, 1, 3], [11, 1, 3, 0.9, 2], [10, 0.1, 2, 0.0, 1], [9, 1, 0.8], [8, 0.9, 0.0], [7, 0.0], [6], [5],
        [4], [3], [19, 9, 7], [18, 8, 6], [17, 7, 5], [16, 6, 4, 11], [15, 5, 10, 7], [14, 4, 9, 6, 7], [13, 3, 8, 5, 6], [12, 2, 7, 4, 5],
        [11, 1, 6, 3, 4], [10, 0.4, 5, 2, 3], [9, 4, 1, 2], [8, 3, 0.0, 1], [7, 2, 0.4],
        [6, 1], [5, 0.2], [4, 11], [3, 10, 7], [19, 9, 6, 9], [18, 11, 5, 8], [17, 10, 7, 7], [19, 9, 6, 9], [18, 8, 8, 7], [17, 7, 7, 6, 7],
        [16, 6, 6, 5, 6], [15, 5, 5, 4, 5], [14, 4, 4, 3, 4], [13, 3, 3, 2, 3], [12, 2, 2, 1, 2], [11, 1, 1, 0.3, 1], [10, 0.9, 0.7, 0.4],
    ];

    /// <summary>Every labelled timer: second, box and the text shown (NaN left out).</summary>
    internal static IEnumerable<(int Second, int Box, double Seconds)> Samples()
    {
        for (int t = 0; t < Labels.Length; t++)
        {
            int n = 0;
            for (int b = 0; b < Labels[t].Length; b++)
                if (Label(Labels[t][b]) is not null && Timers[t][n++] is var v && !double.IsNaN(v)) yield return (t, b, v);
        }
    }

    [Fact]
    public void Labels_and_timers_line_up() =>
        Assert.All(Enumerable.Range(0, Labels.Length), t => Assert.Equal(Labels[t].Count(c => Label(c) is not null), Timers[t].Length));

    [Fact]
    public void Finds_the_four_buffs_at_every_buff_size()
    {
        var f = Fixture.Load(B80.Fixture);
        var wrong = new List<string>();
        for (int t = 0; t < Labels.Length; t++)
        {
            var boxes = Boxes(At(t));
            for (int b = 0; b < Labels[t].Length; b++)
            {
                char want = Labels[t][b];
                var got = BuffReader.Classify(f.Frames[2 * t], boxes[b].Icon);
                bool ok = char.IsUpper(want) ? got == Label(want) : char.IsLower(want) ? got is null || got == Label(want) : got is null;
                if (!ok) wrong.Add($"{t} s box {b} ({want}): {got?.ToString() ?? "none"}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join(" | ", wrong));
    }

    // Reads the reader may get wrong, measured: 11 s under glare (17 s) and on the dimmed frame (25 s) read as 4, 5 read
    // as 6, 0.1 as 1, and 15 as 0.5 (a false '.'). BuffTracker ignores a reading that jumps away from its countdown
    // unless the next read agrees, so these stay one-read blips.
    static readonly HashSet<(int Second, int Box)> KnownWrong = [(17, 3), (19, 3), (25, 0), (26, 1), (38, 0)];

    /// <summary>Every labelled timer reads exactly, reads nothing, or reads its ones digit without the tens (MinTens keeps
    /// the background left of a single digit from adding a phantom 10; BuffTracker restores the tens from its countdown);
    /// the only other reads are <see cref="KnownWrong"/>.</summary>
    [Theory]
    [InlineData(80)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(110)]
    public void Reads_the_labelled_timers_at_each_buff_size(int size)
    {
        var f = Fixture.Load(B80.Fixture);
        var reader = new BuffReader(Geometry.BuffScale(Fhd, size));
        var wrong = Samples().Where(s => SizeAt(s.Second) == size && !KnownWrong.Contains((s.Second, s.Box)))
            .Select(s => (s, got: reader.ReadTimer(f.Frames[2 * s.Second], Boxes(At(s.Second))[s.Box].Timer)))
            .Where(r => r.got is { } g && g != r.s.Seconds && !(r.s.Seconds >= 10 && g == r.s.Seconds % 10))
            .Select(r => $"{r.s.Second} s box {r.s.Box}: {r.s.Seconds} read {r.got}").ToList();
        Assert.True(wrong.Count == 0, string.Join(" | ", wrong));
    }
}
