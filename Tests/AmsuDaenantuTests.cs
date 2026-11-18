using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>암수 in 대난투 (그림자 레이드): the boss debuff row moves under the gimmick's fill bar, centred, its icons smaller and
/// further apart (amsu_daenantu: serka-shadow clip, 1080p, HUD 110, 보스 디버프 크기 120).</summary>
public class AmsuDaenantuTests
{
    static readonly Viewport Hd = new(0, 0, 1920, 1080);
    static readonly Fit Hud110 = new(110.05);

    static Image Cut(int frame, PixelRect s)
    {
        var fixture = Fixture.Load("amsu_daenantu");
        int x = s.X - fixture.Rect[0], y = Math.Max(0, s.Y - fixture.Rect[1]);
        return fixture.Frames[frame].Crop(new PixelRect(x, y, Math.Min(s.Width, fixture.Rect[2] - x), Math.Min(s.Bottom - fixture.Rect[1], fixture.Rect[3]) - y));
    }

    static (Image Strip, AmsuLayout Layout) Frame(int frame) =>
        (Cut(frame, Geometry.DaenantuDebuffStrip(Hd, Hud110)), AmsuLayout.Daenantu(Hd, Hud110));

    [Theory]
    [InlineData(0, 5, 8.0)]   // 17 s: 7 debuffs, 암수 the 6th
    [InlineData(1, 4, 5.0)]   // 20 s: 8 debuffs (the row centred, so it starts half a slot further left), 암수 the 5th
    public void The_bomb_is_found_in_the_centred_row_under_the_gimmick(int frame, int slot, double seconds)
    {
        var (strip, layout) = Frame(frame);
        var bomb = Assert.Single(AmsuReader.Locate(strip, layout.Scale, layout.Slots, layout.Line).Bombs);
        Assert.Equal((Bomb.암수, slot), (bomb.Kind, bomb.Slot));
        Assert.Equal(seconds, AmsuReader.ReadSeconds(strip, bomb, layout.TextScale));
    }

    [Fact]
    public void A_row_without_a_bomb_is_seen_and_holds_none()
    {
        var (strip, layout) = Frame(2);   // 35 s: 7 debuffs, the 5th framed pale (not a debuff frame), passed over
        var find = AmsuReader.Locate(strip, layout.Scale, layout.Slots, layout.Line);
        Assert.Empty(find.Bombs);
        Assert.True(find.Seen);
        Assert.Equal(6, find.Filled);
    }

    [Fact]
    public void The_row_follows_the_resolution_and_stays_centred_on_wide_screens()
    {
        var hd = Geometry.DaenantuDebuffStrip(Hd, Hud110);
        var uhd = Geometry.DaenantuDebuffStrip(new Viewport(0, 0, 3840, 2160), Hud110);
        Assert.InRange(uhd.Y, 2 * hd.Y + 2, 2 * hd.Y + 6);   // the icons' top doubles; the 4 px over the line doesn't
        Assert.InRange(uhd.X + uhd.Width / 2, 2 * (hd.X + hd.Width / 2) - 2, 2 * (hd.X + hd.Width / 2) + 2);
        var wide = Geometry.DaenantuDebuffStrip(new Viewport(0, 0, 2560, 1080), Hud110);   // 21:9: centred on its own middle
        Assert.Equal((hd.Y, hd.Width), (wide.Y, wide.Width));
        Assert.Equal(hd.X + 320, wide.X);
        Assert.Equal(Geometry.DaenantuSlotsIn(Hd, Hud110), Geometry.DaenantuSlotsIn(new Viewport(0, 0, 2560, 1080), Hud110));
        // 10 screen px between frames is 12 reference px at the 100% size (26 px frames on 32 px reference icons)
        var d = Geometry.DaenantuSlotsIn(Hd, Hud110);
        Assert.Equal((12, true, 14), (d.Gap, d.Centred, d.Most));
    }

    [Fact]
    public void The_usual_row_reads_nothing_there()
    {
        // Why 대난투 needs its own layout: the usual strip walks from x 928 on y 169, where 대난투 shows no row.
        var s = Geometry.BossDebuffStrip(Hd, Hud110, 120);
        var strip = Cut(1, s);
        var layout = AmsuLayout.Of(Hd, Hud110, 120);
        int cutTop = Math.Max(s.Y, 180) - s.Y;   // the fixture starts at y 180: the line moves up by what was cut off
        Assert.Empty(AmsuReader.Locate(strip, layout.Scale, layout.Slots, layout.Line - cutTop).Bombs);
    }
}
