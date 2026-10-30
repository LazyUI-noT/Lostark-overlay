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
