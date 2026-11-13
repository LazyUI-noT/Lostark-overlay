using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The easy-read labels (user, 2026-11-05): the boss's HP beside its name, 대난투's damage beside its number.</summary>
public class BossHpLabelTests
{
    static readonly PixelRect At = new(1025, 32, 320, 30);
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Theory]
    [InlineData(469_625_501_361L, 0L, "[4,696억]")]
    [InlineData(1_539_242_432_318L, 0L, "[1조 5,392억]")]
    [InlineData(12_345_678_901_234L, 0L, "[12조 3,456억]")]   // 대난투 into the tens of 조
    [InlineData(15_349_583L, 0L, "[1,534만]")]
    [InlineData(469_625_501_361L, 120_000_000_000L, "[4,696억] +(1,200억)")]
    [InlineData(2_000_000_000_000L, 1_050_000_000_000L, "[2조] +(1조 500억)")]
    public void Amounts_read_in_jo_and_eok_with_the_shield_after(long hp, long shield, string text) =>
        Assert.Equal(text, EasyRead.Of(hp, shield));

    [Fact]
    public void A_bar_read_shows_and_one_missed_read_keeps_it()
    {
        var view = new BossHpView(469_625_501_361, 0, At);
        var label = BossHpLabel.None.Step(view, S(10));
        Assert.Equal(view, label.View);
        Assert.Equal(view, label.Step(null, S(14)).View);   // the 줄 수 창 alone reads the bar every 4 s: no flicker
    }

    [Fact]
    public void No_bar_read_for_a_while_hides_it()
    {
        var label = BossHpLabel.None.Step(new BossHpView(1, 0, At), S(10)).Step(null, S(13)).Step(null, S(15));
        Assert.Null(label.View);
        Assert.Null(BossHpLabel.None.Step(null, S(5)).View);
    }

    [Fact]
    public void The_labels_sit_right_of_the_longest_name_and_the_widest_number()
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110));
        Assert.True(Geometry.BossHpLabel(vp, fit).X >= 1025);   // "광신의 인도자, 아르세노스" ends near 1013
        Assert.True(Geometry.ShadowDealt(vp, fit).X >= 1115);   // "10,000,000,000,000" popped 1.5× ends near 1104
        // On the number's line: its glyphs at y 62–82 (the read area reaches higher, for the pop).
        var dealt = Geometry.ShadowDealt(vp, fit);
        Assert.InRange(dealt.Y + dealt.Height / 2, 71, 73);
    }

    /// <summary>The label follows the name's red text with about a character's gap (boss-label-name.plan.md), on the name's line,
    /// so the name read never sees it; scaled with the HUD and the resolution.</summary>
    [Theory]
    [InlineData(1920, 1080, 110, 1000, 1012)]   // 모르둠's name ends at 1000: the label at 1012, not at the fixed 1025
    [InlineData(1920, 1080, 80, 900, 909)]   // HUD 80: a 9 px gap
    [InlineData(3840, 2160, 110, 2000, 2024)]
    public void The_label_starts_a_character_after_the_name(int w, int h, int hud, int nameRight, int x)
    {
        var (vp, fit) = (new Viewport(0, 0, w, h), new Fit(hud));
        var label = Geometry.BossHpLabelAfter(vp, fit, nameRight);
        Assert.Equal(x, label.X);
        var name = Geometry.BossName(vp, fit);
        var fixedAt = Geometry.BossHpLabel(vp, fit);
        Assert.Equal((name.Y, fixedAt.Width, fixedAt.Height), (label.Y, label.Width, label.Height));
    }

    [Fact]
    public void A_name_near_the_edge_keeps_the_label_on_screen()
    {
        var vp = new Viewport(0, 0, 1920, 1080);
        Assert.Equal(1920, Geometry.BossHpLabelAfter(vp, new Fit(110), 1800).Right);
    }

    [Fact]
    public void The_gimmicks_bar_text_is_lines_and_hp_or_hp_alone()
    {
        Assert.Equal("X 87  4,350억", new ShadowView(435_000_000_000, 87, At, 0, At).Text);
        Assert.Equal("4,350억", new ShadowView(435_000_000_000, null, At, 0, At).Text);
    }
}
