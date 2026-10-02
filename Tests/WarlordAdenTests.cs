using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>워로드 전투태세's C (case 2) shows it is usable by a white key label and a faint purple outline; its icon
/// stays a grey shield, so its middle is only 85–97 lit (45–60 dark), under the brightness 발키리's lit icons reach.
/// war_C (전태.mp4, 10 fps): dark 0–1.5 s and 31–36.5 s, lit 1.6–14 s, 28–29.8 s and 36.6–39.7 s (38.0 s: a one-frame
/// dip, not checked); 32.9–33.6 s dark with the mouse cursor over the label. war_bug_C (전태 버그 아덴키.mp4): lit, gauge
/// full, until the cast at 1.7 s.</summary>
public class WarlordAdenTests
{
    static readonly (double From, double To)[] Dark = [(0, 1.5), (31.0, 36.5)];
    static readonly (double From, double To)[] Lit = [(1.6, 14.0), (28.0, 29.8), (36.6, 39.7)];

    static IEnumerable<(double T, ReadKind Kind)> Read(string name, DarkCue cue)
    {
        var f = Fixture.Load(name);
        var digits = new DigitReader(Geometry.Scale(Fhd, new Fit(f.Hud)));
        return f.Frames.Select((img, i) => (Math.Round(f.Pts[i].TotalSeconds, 1), SlotClassifier.Read(SlotId.아덴2, img, f.Pts[i], null, digits, cue).Kind));
    }

    static bool In((double From, double To)[] spans, double t) => spans.Any(s => t >= s.From && t <= s.To);

    [Fact]
    public void A_lit_C_reads_ready_and_a_dark_one_unavailable_by_its_key_label()
    {
        var reads = Read("war_C", DarkCue.Label).Where(r => r.T != 38.0).ToList();
        Assert.All(reads.Where(r => In(Lit, r.T)), r => Assert.True(r.Kind == ReadKind.Ready, $"{r.T}s lit read {r.Kind}"));
        Assert.All(reads.Where(r => In(Dark, r.T)), r => Assert.True(r.Kind == ReadKind.Unavailable, $"{r.T}s dark read {r.Kind}"));
    }

    [Fact]
    public void The_recorded_bug_C_with_a_full_gauge_reads_ready_until_it_is_cast() =>
        Assert.All(Read("war_bug_C", DarkCue.Label).Where(r => r.T <= 1.7), r => Assert.True(r.Kind == ReadKind.Ready, $"{r.T}s read {r.Kind}"));

    /// <summary>The bug: 발키리's icon test calls every lit C dark.</summary>
    [Fact]
    public void The_icon_test_alone_reads_a_lit_C_as_dark() =>
        Assert.All(Read("war_bug_C", DarkCue.Icon).Where(r => r.T <= 1.7), r => Assert.Equal(ReadKind.Unavailable, r.Kind));
}
