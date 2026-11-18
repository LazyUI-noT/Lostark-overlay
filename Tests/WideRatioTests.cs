using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>rail-split.plan.md item 6: on a wider game area at the same height, every area the game draws from the centre moves by
/// exactly the extra width's half, the right-edge ones by all of it, and 던전 stays at the left edge. Widths with an even extra
/// (2560 is 21:9, 3840 is 32:9), so half of it is whole pixels and no rounding hides a drift.</summary>
public class WideRatioTests
{
    static readonly Viewport Base = new(0, 0, 1920, 1080);
    static readonly Fit HudFit = new(110);
    const int BuffSize = 120;

    public static TheoryData<int> Widths => new() { 2560, 3840 };

    static readonly (string Name, Func<Viewport, PixelRect> Area)[] Centred =
    [
        ("boss text row", vp => Geometry.BossTextRow(vp, HudFit)),
        ("boss bar", vp => Geometry.BossBar(vp, HudFit)),
        ("boss hp text", vp => Geometry.BossHpText(vp, HudFit)),
        ("boss count", vp => Geometry.BossCount(vp, HudFit)),
        ("boss name", vp => Geometry.BossName(vp, HudFit)),
        ("boss effect row", vp => Geometry.BossRow(vp, HudFit)),
        ("암수 strip", vp => Geometry.BossDebuffStrip(vp, HudFit)),
        ("대난투 strip", vp => Geometry.DaenantuDebuffStrip(vp, HudFit)),
        ("대난투 체력", vp => Geometry.ShadowNumber(vp, HudFit)),
        ("skill bar", vp => Geometry.BarRect(vp, HudFit)),
        ("buff row", vp => Geometry.BuffArea(vp, HudFit, BuffSize)),
        ("debuff row", vp => Geometry.DebuffArea(vp, HudFit, BuffSize)),
        ("단심 band", Geometry.JudgmentBand),
        ("menu panel", vp => Geometry.MenuArea(vp, Geometry.RefMenuPanel)),
        ("loading bar", Geometry.LoadingBar),
    ];

    [Theory, MemberData(nameof(Widths))]
    public void Centred_areas_move_by_half_the_extra_width(int width)
    {
        var wide = Base with { Width = width };
        int pad = (width - Base.Width) / 2;
        foreach (var (name, area) in Centred)
            Assert.Equal((name, area(Base) with { X = area(Base).X + pad }), (name, area(wide)));
    }

    /// <summary>16:7 at other heights (the plan's 2560×1120, 3840×1680): the 16:9 width there isn't whole, so each area keeps its
    /// offset from the centre within the 1 px a rounding can move it.</summary>
    [Theory]
    [InlineData(2560, 1120)]
    [InlineData(3840, 1680)]
    public void Centred_areas_keep_their_offset_from_the_centre_at_16_7(int width, int height)
    {
        var wide = new Viewport(0, 0, width, height);
        var narrow = new Viewport(0, 0, (int)Math.Round(height * 16 / 9.0), height);
        foreach (var (name, area) in Centred)
        {
            PixelRect a = area(narrow), b = area(wide);
            double drift = (b.X - width / 2.0) - (a.X - narrow.Width / 2.0);
            // The 암수 strip rounds its two edges apart, so its width may differ by 1 px too.
            Assert.True(Math.Abs(drift) <= 1 && a.Y == b.Y && Math.Abs(a.Width - b.Width) <= 1 && a.Height == b.Height, $"{name}: {a} vs {b}");
        }
    }

    [Theory, MemberData(nameof(Widths))]
    public void Right_edge_areas_move_by_the_whole_extra_width(int width)
    {
        var wide = Base with { Width = width };
        int extra = width - Base.Width;
        Assert.Equal(Geometry.ChannelCorner(Base) with { X = Geometry.ChannelCorner(Base).X + extra }, Geometry.ChannelCorner(wide));
        Assert.Equal(Geometry.MenuWaiting(Base) with { X = Geometry.MenuWaiting(Base).X + extra }, Geometry.MenuWaiting(wide));
    }

    [Theory, MemberData(nameof(Widths))]
    public void The_dungeon_panel_stays_at_the_left_edge(int width) =>
        Assert.Equal(Geometry.DungeonPanel(Base, HudFit), Geometry.DungeonPanel(Base with { Width = width }, HudFit));

    /// <summary>A real read: the 1920 boss shot set in the middle of a wider game area still gives the same HP and count.</summary>
    [Theory, MemberData(nameof(Widths))]
    public void The_boss_bar_reads_the_same_on_a_wider_screen(int width)
    {
        var f = Fixture.Load("bosstop_mordum");
        int pad = (width - Base.Width) / 2;
        var vp = Base with { Width = width };
        var band = Geometry.BossTextRow(vp, HudFit);
        var bar = BossDigits.ReadBar(f.Frames[0].Crop(band with { X = band.X - f.Rect[0] - pad, Y = band.Y - f.Rect[1] }), Geometry.Scale(vp, HudFit));
        Assert.Equal((16_897_047_862_607L, 17_126_566_042_900L), bar.Hp is { } hp ? (hp.Current, hp.Max) : default((long, long)?));
        Assert.Equal(494, bar.Count);
    }
}
