using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The boss HP bar and its text (Geometry.BossBar, BossCount, BossHpText, BossName) on the top-of-screen shots
/// (tools/make-fixtures.ps1, "bosstop_*"): the raid bar (모르둠, no portrait at 16:9, one at 21:9) and the dummy's narrower bar.</summary>
public class BossBarGeometryTests
{
    // Fixture, viewport top and height, HUD, and whether it is a raid bar (its right end is the bar's, and it shows "X n").
    public static TheoryData<string, int, int, double, bool> Shots => new()
    {
        { "bosstop_mordum", 0, 1080, 110, true },
        { "bosstop_layout", 0, 1080, 110, true },
        { "bosstop_219", 135, 810, 110, true },
        { "bosstop_dummy100", 0, 1080, 100, false },
        { "bosstop_dummy80", 0, 1080, 80, false },
    };

    /// <summary>The fixture's frame, and a reader for a screen pixel in it.</summary>
    static Func<int, int, (byte B, byte G, byte R)> Screen(string name)
    {
        var f = Fixture.Load(name);
        var img = f.Frames[0];
        return (x, y) => img.Pixel(x - f.Rect[0], y - f.Rect[1]);
    }

    static int Sum((byte B, byte G, byte R) p) => p.B + p.G + p.R;

    [Theory, MemberData(nameof(Shots))]
    public void The_bar_rows_are_the_fill_between_the_dark_frame_rows(string name, int top, int height, double hud, bool raid)
    {
        _ = raid;
        var (vp, fit) = (new Viewport(0, top, 1920, height), new Fit(hud));
        var px = Screen(name);
        var bar = Geometry.BossBar(vp, fit);
        int x = (int)Math.Round(960 + (1150 - 960) * Geometry.Scale(vp, fit)), mid = bar.Y + bar.Height / 2;   // no text there
        int t = mid, b = mid;
        while (Sum(px(x, t - 1)) > 150) t--;
        while (Sum(px(x, b + 1)) > 150) b++;
        Assert.InRange(t, bar.Y - 2, bar.Y + 2);
        Assert.InRange(b, bar.Bottom - 1 - 2, bar.Bottom - 1 + 2);
    }

    [Theory, MemberData(nameof(Shots))]
    public void A_raid_bar_ends_where_the_rect_does(string name, int top, int height, double hud, bool raid)
    {
        if (!raid) return;   // the dummy's bar is a field boss's, shorter
        var (vp, fit) = (new Viewport(0, top, 1920, height), new Fit(hud));
        var px = Screen(name);
        var bar = Geometry.BossBar(vp, fit);
        int y = bar.Y + 3, x = (int)Math.Round(bar.Right - 64 * Geometry.Scale(vp, fit));
        var c = px(x, y);
        static int D((byte B, byte G, byte R) p, (byte B, byte G, byte R) q) => Math.Abs(p.B - q.B) + Math.Abs(p.G - q.G) + Math.Abs(p.R - q.R);
        while (D(px(x + 1, y), c) < 90) x++;
        Assert.InRange(x, bar.Right - 1 - 3, bar.Right - 1 + 3);   // 모르둠's orange ends in a 3 px gold glow
    }

    /// <summary>The bounds of the pixels <paramref name="hit"/> picks inside <paramref name="area"/>.</summary>
    static PixelRect Ink(Func<int, int, (byte B, byte G, byte R)> px, PixelRect area, Func<(byte B, byte G, byte R), bool> hit)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
                if (hit(px(x, y))) (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        Assert.True(x1 >= 0, "no text found");
        return new(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    static bool White((byte B, byte G, byte R) p) => Math.Min(p.B, Math.Min(p.G, p.R)) > 200;
    static bool Red((byte B, byte G, byte R) p) => p.R > 170 && p.G < 110 && p.B < 110;

    static void Inside(PixelRect inner, PixelRect outer) =>
        Assert.True(inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom, $"{inner} not in {outer}");

    [Theory, MemberData(nameof(Shots))]
    public void The_count_the_hp_and_the_name_lie_in_their_boxes(string name, int top, int height, double hud, bool raid)
    {
        var (vp, fit) = (new Viewport(0, top, 1920, height), new Fit(hud));
        var px = Screen(name);
        var (bar, count, hp, title) = (Geometry.BossBar(vp, fit), Geometry.BossCount(vp, fit), Geometry.BossHpText(vp, fit), Geometry.BossName(vp, fit));
        double k = Geometry.Scale(vp, fit);
        int X(double refX) => (int)Math.Round(960 + (refX - 960) * k);
        if (raid) Inside(Ink(px, bar with { X = X(1236), Width = bar.Right - X(1236) }, White), count);
        Inside(Ink(px, bar with { X = X(640), Width = X(1236) - X(640) }, White), hp);
        int above = (int)(38 * k);   // the name, not the bar or the coins row over it
        Inside(Ink(px, new(X(700), bar.Y - above, X(1200) - X(700), above), Red), title);
        Assert.True(hp.Right <= count.X && title.Bottom <= bar.Y, "the boxes overlap");
    }

    [Fact]
    public void The_boss_shift_moves_all_four()
    {
        var moved = new Fit(110, BossShift: new Shift(5, -3));
        foreach (var f in new Func<Viewport, Fit, PixelRect>[] { Geometry.BossBar, Geometry.BossCount, Geometry.BossHpText, Geometry.BossName })
        {
            var at = f(Fhd, new Fit(110));
            Assert.Equal(at with { X = at.X + 5, Y = at.Y - 3 }, f(Fhd, moved));
        }
    }
}
