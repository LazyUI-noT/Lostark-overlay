using System.Globalization;
using System.Text;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The boss row's "N초" at 보스 디버프 크기 80% and 90% (1080p, HUD 110): bosstext_8090, 빛암 80_90.mp4 at 1 fps,
/// cut at (900, 160). Read with the boss glyphs (BossGlyphs), learned from these labels.</summary>
public class BossTimerTests
{
    static readonly Fit Hud110 = new(110);
    const int CropX = 900, CropY = 160;

    /// <summary>The size on screen at <paramref name="second"/>: 80% to 48 s, the settings window 50–53 s, 90% from 54 s.</summary>
    internal static int SizeAt(int second) => second <= 48 ? 80 : 90;

    // Per second, each debuff slot's text from the first slot ('_': an icon without text). Read off 3× zooms; seconds left out
    // are cutscenes, menus, glare or a damage number over the text (15–18, 38, 49, 56), or "0.0초" (35).
    internal static readonly Dictionary<int, string> Labels = new()
    {
        [11] = "5 24", [12] = "5 23", [13] = "4 22", [14] = "3 21",
        [19] = "16", [20] = "15", [21] = "14", [22] = "13",
        [23] = "12 5 9", [24] = "11 4 8", [25] = "10 3 7", [26] = "9 2 6", [27] = "8 1 5", [28] = "7 0.5 4", [29] = "6 _ 3",
        [30] = "5 2", [31] = "4 1", [32] = "3 0.5", [33] = "2 _", [34] = "1",
        [39] = "4 8", [40] = "5 7", [41] = "5 6 24", [42] = "4 5 23", [43] = "3 4 22", [44] = "2 3 21", [45] = "1 2 20",
        [46] = "0.2 1 19", [47] = "0.9 18", [48] = "_ 17",
        [54] = "11", [55] = "10", [57] = "8", [58] = "7", [59] = "6", [60] = "5", [61] = "4", [62] = "3", [63] = "2", [64] = "1",
        [65] = "0.2",
        [72] = "5 24", [73] = "4 23", [74] = "3 22", [75] = "2 21", [76] = "1 20", [77] = "0.6 19", [78] = "_ 18",
        [79] = "17", [80] = "16", [81] = "15", [82] = "14", [83] = "13", [84] = "12", [85] = "11", [86] = "10", [87] = "9",
        [88] = "8", [89] = "7", [90] = "6", [91] = "5", [92] = "4", [93] = "3", [94] = "2", [95] = "1", [96] = "0.6",
    };

    /// <summary>Every labelled text: the second (= frame), the slot's icon in fixture px, the size and the seconds shown.</summary>
    internal static IEnumerable<(int Second, PixelRect Icon, int Size, double Seconds)> Samples()
    {
        foreach (var (second, line) in Labels.OrderBy(l => l.Key))
        {
            int size = SizeAt(second);
            var slots = Geometry.BossDebuffSlots(Fhd, Hud110, size);
            var texts = line.Split(' ');
            for (int i = 0; i < texts.Length; i++)
                if (texts[i] != "_")
                {
                    var icon = slots[i].Icon;
                    yield return (second, icon with { X = icon.X - CropX, Y = icon.Y - CropY }, size,
                        double.Parse(texts[i], CultureInfo.InvariantCulture));
                }
        }
    }

    // Unread, never wrong: "0.9초" over glare at 47 s. Held out (learned on odd seconds, read on even) one read was wrong, 28 s
    // "0.5초" as 5: Amsu.Seen keeps a running bomb's countdown against a read that jumps up, so it stays at its last second.
    static readonly HashSet<int> KnownUnread = [47];

    [Theory]
    [InlineData(80)]
    [InlineData(90)]
    public void Reads_every_labelled_boss_timer(int size)
    {
        var f = Fixture.Load("bosstext_8090");
        double text = Geometry.BuffScale(Fhd, size);
        var wrong = Samples().Where(s => s.Size == size && (Environment.GetEnvironmentVariable("GAUGE_HALF") is not { } h || s.Second % 2 != int.Parse(h)))
            .Select(s => (s, got: AmsuReader.ReadSeconds(f.Frames[s.Second], new BombAt(Bomb.빛암, s.Icon), text)))
            .Where(r => r.got != r.s.Seconds && !(r.got is null && KnownUnread.Contains(r.s.Second)))
            .Select(r => $"{r.s.Second} s: {r.s.Seconds} read {r.got?.ToString() ?? "null"}").ToList();
        Assert.True(wrong.Count == 0, string.Join(" | ", wrong));
    }

    [Fact]
    public void A_slot_without_text_reads_nothing()
    {
        // The '_' slots (an icon whose text is gone) and the next two empty slots right of each labelled row.
        var f = Fixture.Load("bosstext_8090");
        var read = new List<string>();
        foreach (var (second, line) in Labels)
        {
            int size = SizeAt(second);
            var slots = Geometry.BossDebuffSlots(Fhd, Hud110, size);
            var texts = line.Split(' ');
            foreach (int i in Enumerable.Range(0, texts.Length + 2).Where(i => i >= texts.Length || texts[i] == "_"))
            {
                var icon = slots[i].Icon;
                var got = AmsuReader.ReadSeconds(f.Frames[second], new BombAt(Bomb.빛암, icon with { X = icon.X - CropX, Y = icon.Y - CropY }),
                    Geometry.BuffScale(Fhd, size));
                if (got is not null) read.Add($"{second} s slot {i}: {got}");
            }
        }
        Assert.True(read.Count == 0, string.Join(" | ", read));
    }

    /// <summary>Rewrites Core/Vision/BossGlyphs.cs as Build_buff_glyphs_when_requested does BuffGlyphs.cs, from the labelled boss
    /// text, each size enlarged by <see cref="Enlarge"/>. Runs only with GAUGE_BUILD_TEMPLATES=1.</summary>
    [Fact]
    public void Build_boss_glyphs_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TEMPLATES") != "1") return;
        var f = Fixture.Load("bosstext_8090");
        var samples = Samples().Where(s => Environment.GetEnvironmentVariable("GAUGE_HALF") is not { } h || s.Second % 2 == int.Parse(h)).Select(s => (f.Frames[s.Second], AmsuReader.TimerWindow(f.Frames[s.Second], s.Icon, Geometry.BuffScale(Fhd, s.Size)),
            s.Size, s.Seconds));
        string code = BuffTimerTests.GlyphSource(samples, size => Enlarge[size], "BossGlyphs",
            "The boss row's \"N초\" glyphs (암수, 빛암), one set per 보스 디버프 크기 at 1080: per-pixel mean luminance of the labelled",
            "boss timers in 빛암 80_90.mp4 (BossTimerTests), each enlarged by the set's Enlarge to the buff glyphs' 120% cells. Generated by",
            "BossTimerTests.Build_boss_glyphs_when_requested (GAUGE_BUILD_TEMPLATES=1); do not edit by hand.");
        File.WriteAllText(Path.Combine(Helpers.RepoRoot(), "Core", "Vision", "BossGlyphs.cs"), code);
    }

    // The boss text runs ×1.05 a buff timer's at the same size (AmsuReader's text lead), so the buff enlargement ÷ 1.05.
    static readonly Dictionary<int, double> Enlarge = new() { [80] = 1.74, [90] = 1.45 };
}
