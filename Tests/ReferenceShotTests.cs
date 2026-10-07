using System.Text.Json.Nodes;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>--build-gauge takes screenshots at any resolution, HUD size and 21:9: each is fitted like 스킬창 맞추기 and
/// resampled to the reference screen (1920×1080, HUD 110%) the gauge masks are made in.</summary>
public class ReferenceShotTests
{
    /// <summary>A whole screenshot around a band fixture: the game area grey (not black, or it reads as a letterbox bar), black
    /// bars above and below it, and the band where it was captured, shifted by the game area's origin on that monitor.</summary>
    static Image Screen(string band, int width, int height, Viewport game, int originX = 0, int originY = 0)
    {
        var f = Fixture.Load(band);
        var img = new Image(width, height);
        for (int y = game.Y; y < game.Y + game.Height; y++)
            for (int x = game.X; x < game.X + game.Width; x++)
            {
                int i = y * img.Stride + x * 4;
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (30, 30, 30, 255);
            }
        Paste(img, f.Frames[0], f.Rect[0] - originX, f.Rect[1] - originY);
        return img;
    }

    static void Paste(Image to, Image from, int x0, int y0)
    {
        for (int y = 0; y < from.Height; y++)
            from.Row(y).CopyTo(to.Bgra.AsSpan((y0 + y) * to.Stride + x0 * 4, from.Width * 4));
    }

    static void Fill(Image img, PixelRect r, (byte B, byte G, byte R) c)
    {
        for (int y = r.Y; y < r.Y + r.Height; y++)
            for (int x = r.X; x < r.X + r.Width; x++)
            {
                int i = y * img.Stride + x * 4;
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (c.B, c.G, c.R, 255);
            }
    }

    public static TheoryData<string, int, int, int, int, int, int, int, int, double> Shots => new()
    {
        // band, screenshot size, game area (x, y, w, h), the game's origin on the monitor it was captured on, HUD
        { "band_110", 1920, 1080, 0, 0, 1920, 1080, 0, 0, 110 },
        { "band_80", 1920, 1080, 0, 0, 1920, 1080, 0, 0, 80 },
        { "band_1440", 2560, 1440, 0, 0, 2560, 1440, 1080, 162, 100 },
        { "band_4k", 3840, 2160, 0, 0, 3840, 2160, 0, 0, 100 },
        { "band_219", 1920, 1080, 0, 135, 1920, 810, 0, 0, 110 },
        { "band_4k219", 3840, 2160, 0, 270, 3840, 1620, 0, 0, 100 },
    };

    [Theory]
    [MemberData(nameof(Shots))]
    public void Finds_the_game_area_and_HUD_size_of_any_screenshot(string band, int w, int h, int gx, int gy, int gw, int gh, int ox, int oy, double hud)
    {
        var found = ReferenceShot.Find(Screen(band, w, h, new Viewport(gx, gy, gw, gh), ox, oy));
        Assert.NotNull(found);
        Assert.Equal(new Viewport(gx, gy, gw, gh), found.Value.Vp);
        Assert.InRange(found.Value.Fit.Hud, hud - 2, hud + 2);
    }

    [Theory]
    [MemberData(nameof(Shots))]
    public void A_screenshot_resampled_to_the_reference_looks_like_a_1080p_HUD_110_shot(string band, int w, int h, int gx, int gy, int gw, int gh, int ox, int oy, double hud)
    {
        _ = hud;
        var shot = Screen(band, w, h, new Viewport(gx, gy, gw, gh), ox, oy);
        var (vp, fit) = ReferenceShot.Find(shot)!.Value;
        var reference = ReferenceShot.ToReference(shot, vp, fit);

        Assert.Equal((ReferenceShot.Width, ReferenceShot.Height), (reference.Width, reference.Height));
        var again = ReferenceShot.Find(reference);
        Assert.NotNull(again);
        Assert.Equal(new Viewport(0, 0, 1920, 1080), again.Value.Vp);
        Assert.InRange(again.Value.Fit.Hud, 108, 112);
    }

    [Fact]
    public void A_gauge_painted_on_a_1440p_HUD_100_pair_is_built_where_it_sits_in_reference_pixels()
    {
        var gauge = new PixelRect(930, 1052, 60, 8);   // reference pixels, inside the band
        var empty = Screen("band_1440", 2560, 1440, new Viewport(0, 0, 2560, 1440), 1080, 162);
        var full = Screen("band_1440", 2560, 1440, new Viewport(0, 0, 2560, 1440), 1080, 162);
        var (vp, fit) = ReferenceShot.Find(full)!.Value;
        Fill(full, Geometry.ToScreen(gauge, vp, fit), (40, 200, 240));

        var (e, f, _) = ReferenceShot.Normalize(empty, full);
        var (json, mask) = GaugeLoader.Build(e, f, new PixelRect(900, 1030, 120, 45));

        var origin = JsonNode.Parse(json)!["origin"]!.AsArray();
        Assert.InRange((int)origin[0]!, gauge.X - 2, gauge.X + 2);
        Assert.InRange((int)origin[1]!, gauge.Y - 2, gauge.Y + 2);
        Assert.InRange(mask.Width, gauge.Width - 3, gauge.Width + 3);
        Assert.InRange(mask.Height, gauge.Height - 3, gauge.Height + 3);
    }

    [Fact]
    public void When_only_one_shot_shows_the_skill_bar_its_fit_is_used_for_both()
    {
        var withBar = Screen("band_110", 1920, 1080, new Viewport(0, 0, 1920, 1080));
        var withoutBar = new Image(1920, 1080);
        var (e, f, note) = ReferenceShot.Normalize(withoutBar, withBar);
        Assert.Equal((1920, 1080), (e.Width, e.Height));
        Assert.Equal((1920, 1080), (f.Width, f.Height));
        Assert.Contains("HUD", note);
        Assert.Contains("한 장", note);   // says the bar-less shot took the other's fit
    }

    [Fact]
    public void The_emblem_is_cut_from_a_reference_shot_at_its_reference_rect()
    {
        var shot = Screen("band_110", 1920, 1080, new Viewport(0, 0, 1920, 1080));
        var emblem = ReferenceShot.Emblem(shot);
        var r = Geometry.RefEmblem;
        Assert.Equal((r.Width, r.Height), (emblem.Width, emblem.Height));
        Assert.Equal(shot.Pixel(r.X + 10, r.Y + 20), emblem.Pixel(10, 20));
    }

    [Fact]
    public void No_skill_bar_in_either_shot_is_a_clear_error()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ReferenceShot.Normalize(new Image(1920, 1080), new Image(1920, 1080)));
        Assert.Contains("스킬창", ex.Message);
    }

    [Fact]
    public void One_shot_without_the_bar_at_another_size_is_refused()
    {
        var withBar = Screen("band_110", 1920, 1080, new Viewport(0, 0, 1920, 1080));
        Assert.Throws<InvalidDataException>(() => ReferenceShot.Normalize(withBar, new Image(2560, 1440)));
    }
}
