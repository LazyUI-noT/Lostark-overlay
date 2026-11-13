using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The boss's name above the bar picks the gate (BossName). The template is 모르둠's name from mordum.png
/// (Profiles/bossnames/act3-g3.bgra); the layout shots (another frame, 21:9 at 0.75) are read with it. On the five shots the
/// same name scores 0.74+ (21:9) and 루메루스 near 0, and its ink is 66 reference px wide against 167.</summary>
public class BossNameTests
{
    static readonly string Profiles = Path.Combine(RepoRoot(), "Profiles");

    static (Image Strip, double Scale) Strip(string name, int top, int height, double hud)
    {
        var f = Fixture.Load(name);
        var (vp, fit) = (new Viewport(0, top, 1920, height), new Fit(hud));
        var r = Geometry.BossName(vp, fit);
        return (f.Frames[0].Crop(r with { X = r.X - f.Rect[0], Y = r.Y - f.Rect[1] }), Geometry.Scale(vp, fit));
    }

    static IReadOnlyList<(string, Image)> Templates() => BossName.LoadTemplates(Profiles, RaidData.Load(Profiles));

    [Theory]
    [InlineData("bosstop_mordum", 0, 1080, 110, "act3-g3")]
    [InlineData("bosstop_layout", 0, 1080, 110, "act3-g3")]
    [InlineData("bosstop_219", 135, 810, 110, "act3-g3")]
    [InlineData("bosstop_dummy100", 0, 1080, 100, null)]
    [InlineData("bosstop_dummy80", 0, 1080, 80, null)]
    public void The_name_picks_the_gate(string name, int top, int height, double hud, string? gate)
    {
        var (strip, scale) = Strip(name, top, height, hud);
        Assert.Equal(gate, BossName.Match(strip, scale, Templates()));
    }

    [Fact]
    public void A_blank_strip_or_no_templates_match_nothing()
    {
        Assert.Null(BossName.Match(new Image(400, 30), 1, Templates()));
        var (strip, scale) = Strip("bosstop_mordum", 0, 1080, 110);
        Assert.Null(BossName.Match(strip, scale, []));
    }

    [Fact]
    public void A_gate_without_a_name_file_is_left_out()
    {
        RaidGate other = new("nowhere", "x", "y", [], [], Raid.없음);
        Assert.Equal(["act3-g3"], BossName.LoadTemplates(Profiles, [.. RaidData.Load(Profiles), other]).Select(t => t.GateId));
    }

    /// <summary>Where the name's red text ends in its strip, for the HP label beside it (boss-label-name.plan.md): the ink's right
    /// edge on all five shots (16:9, 21:9 at 0.75, HUD 100 and 80).</summary>
    [Theory]
    [InlineData("bosstop_mordum", 0, 1080, 110, 280)]
    [InlineData("bosstop_layout", 0, 1080, 110, 281)]
    [InlineData("bosstop_219", 135, 810, 110, 209)]
    [InlineData("bosstop_dummy100", 0, 1080, 100, 209)]
    [InlineData("bosstop_dummy80", 0, 1080, 80, 167)]
    public void The_name_ends_where_its_red_text_ends(string name, int top, int height, double hud, int end)
    {
        var (strip, scale) = Strip(name, top, height, hud);
        Assert.Equal(end, BossName.TextEnd(strip, scale));
    }

    [Fact]
    public void Red_specks_are_no_name_and_dont_lengthen_one()
    {
        var specks = new Image(400, 30);
        foreach (var x in new[] { 50, 51, 300 }) Paint(specks, x, 10);
        Assert.Null(BossName.TextEnd(specks, 1));   // a flash's few red pixels: no name, so no label
        Assert.Null(BossName.TextEnd(new Image(400, 30), 1));
        var (strip, scale) = Strip("bosstop_mordum", 0, 1080, 110);
        Paint(strip, 380, 12);
        Paint(strip, 381, 12);
        Assert.Equal(280, BossName.TextEnd(strip, scale));   // a speck 100 px past the name stays out of it
    }

    /// <summary>OCR reads only the red name: no red text (대난투, a cutscene) means no read at all, never the whole strip with the
    /// app's own label in it ("[7,568역1", 세르카 대난투 replay 2026-11-12).</summary>
    [Fact]
    public void Ocr_reads_up_to_the_name_and_nothing_without_one()
    {
        Assert.Null(BossName.OcrStrip(new Image(400, 30), 1));
        var (strip, scale) = Strip("bosstop_mordum", 0, 1080, 110);
        var read = BossName.OcrStrip(strip, scale);
        Assert.NotNull(read);
        Assert.Equal(280 + (int)Math.Ceiling(4 * scale), read.Width);
        Assert.Equal(strip.Height, read.Height);
    }

    static void Paint(Image img, int x, int y)
    {
        int i = (y * img.Width + x) * 4;
        (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (0, 0, 255, 255);
    }

    /// <summary>Writes Profiles/bossnames/act3-g3.bgra: the name's red ink in mordum.png at HUD 110 (GAUGE_BUILD_TEMPLATES=1).</summary>
    [Fact]
    public void Build_boss_name_templates_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_BUILD_TEMPLATES") != "1") return;
        var (strip, _) = Strip("bosstop_mordum", 0, 1080, 110);
        var ink = BossName.Ink(strip) ?? throw new InvalidOperationException("no name in mordum");
        Directory.CreateDirectory(Path.Combine(Profiles, "bossnames"));
        File.WriteAllBytes(Path.Combine(Profiles, "bossnames", "act3-g3.bgra"), strip.Crop(ink).ToBytes());
    }
}
