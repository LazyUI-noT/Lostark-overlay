using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>전분 위치 찾기 (spec § C): the panel found anywhere on the game screen from its green row, kept only when it reads.</summary>
public sealed class JeonbunFinderTests
{
    static Image Panel(int i) => Fixture.Load($"jeonbun_{i}").Frames[0];

    /// <summary>A dark 1280×720 screen with <paramref name="panel"/> pasted at (x, y).</summary>
    static Image Screen(Image panel, int x, int y)
    {
        var s = new Image(1280, 720);
        for (int i = 0; i < s.Bgra.Length; i += 4) (s.Bgra[i], s.Bgra[i + 1], s.Bgra[i + 2], s.Bgra[i + 3]) = (40, 32, 30, 255);
        for (int py = 0; py < panel.Height; py++)
            panel.Row(py).CopyTo(s.Bgra.AsSpan((y + py) * s.Stride + x * 4, panel.Width * 4));
        return s;
    }

    [Theory]
    [InlineData(0, 700, 620)]    // near the bottom: no room for a second row below
    [InlineData(19, 120, 200)]   // anywhere else
    [InlineData(15, 500, 40)]    // the support view, a flash behind it
    [InlineData(22, 600, 300)]   // two rows (mine second), a % column
    public void The_panel_is_found_where_it_is(int fixture, int x, int y)
    {
        var found = JeonbunFinder.Find(Screen(Panel(fixture), x, y), 1.0);
        Assert.NotNull(found);
        Assert.InRange(found!.Value.X, x - 2, x + 2);
        Assert.InRange(found.Value.Y, y - 2, y + 2);
        Assert.Equal(470, found.Value.Width);
        Assert.InRange(found.Value.Height, 88, 114);
    }

    [Fact]
    public void The_found_spot_reads_as_the_panel_itself()
    {
        var screen = Screen(Panel(2), 300, 300);
        var r = JeonbunFinder.Find(screen, 1.0)!.Value;
        Assert.Equal(JeonbunReader.Read(Panel(2)), JeonbunReader.Read(ImageOps.Crop(screen, r)));
    }

    [Theory]
    [InlineData(16)]   // a lobby view
    [InlineData(17)]   // the 보관 기록 view: no green row yet, so nothing until the fight starts
    public void Nothing_that_does_not_read_is_found(int fixture)
    {
        Assert.Null(JeonbunFinder.Find(Screen(Panel(fixture), 400, 300), 1.0));
    }

    [Fact]
    public void A_screen_without_the_panel_finds_nothing() => Assert.Null(JeonbunFinder.Find(new Image(1280, 720), 1.0));

    // 테스트 모드: a cropped or zoomed video shows the panel at a size the screen's height doesn't give (user, 2026-11-12).
    // A real frame's corner (serka-g1-90s at 20 s): the panel at (128, 67) with the game scene around it. Pasted crops on a
    // flat screen won't do: there a crop too large still reads, in real frames none does.
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.33)]   // a 1440×810 crop of a 1080p video shown at 1920×1080
    [InlineData(1.6)]
    [InlineData(0.8)]
    public void A_panel_at_any_size_is_found_by_measuring_it(double f)
    {
        var scene = Fixture.Load("jeonbun_scene").Frames[0];
        var screen = ImageOps.Resample(scene, new PixelRect(0, 0, scene.Width, scene.Height), f);
        var found = JeonbunFinder.FindAnySize(screen);
        Assert.NotNull(found);
        Assert.InRange(found!.Value.X, 128 * f - 3, 128 * f + 3);
        Assert.InRange(found.Value.Y, 67 * f - 3, 67 * f + 3);
        Assert.InRange(found.Value.Width, 470 * f * 0.98, 470 * f * 1.02);
        if (f >= 1)   // at 0.8 (376 px) the text is too small for the reader, the finder still places it
            Assert.Equal(208, JeonbunReader.ReadSoft(ImageOps.Crop(screen, found.Value))?.Timer);   // 03:28, read as 테스트 모드 reads
    }

    // The same frame at its own size: the green cell starts a pixel above where the text puts it, and the crop that high
    // doesn't read; the finder tries a pixel or two down.
    [Fact]
    public void The_usual_size_finds_a_panel_whose_cell_sits_a_pixel_high()
    {
        var found = JeonbunFinder.Find(Fixture.Load("jeonbun_scene").Frames[0], 1.0);
        Assert.NotNull(found);
        Assert.InRange(found!.Value.Y, 67, 69);
    }

    // A Discord stream (discord_전분_sample.png): blur loses the green cell's '8' and pales the timer, so nothing reads in
    // full, but the panel's frame (timer, titles, green row) shows. Pasted on a dark screen, and alone, 12 px from the screen's right edge (the larger sizes measured run past it).
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_streamed_panel_is_found_by_its_frame(int edge)
    {
        var scene = Fixture.Load("jeonbun_discord").Frames[0];
        var screen = edge == 1 ? scene : Screen(scene, 300, 200);
        int x0 = edge == 1 ? 0 : 300, y0 = edge == 1 ? 0 : 200;
        Assert.Null(JeonbunFinder.Find(screen, 1.0));
        var found = JeonbunFinder.FindAnySize(screen);
        Assert.NotNull(found);
        Assert.InRange(found!.Value.X, x0 + 118 - 3, x0 + 118 + 3);
        Assert.InRange(found.Value.Y, y0 + 91 - 3, y0 + 91 + 3);
        Assert.InRange(found.Value.Width, 470 * 0.97, 470 * 1.03);
    }

    [Fact]
    public void The_usual_size_misses_a_zoomed_panel() =>
        Assert.Null(JeonbunFinder.Find(ImageOps.Resample(Fixture.Load("jeonbun_scene").Frames[0], new PixelRect(0, 0, 600, 260), 1.33), 1.0));

    [Fact]
    public void Any_size_finds_nothing_on_a_screen_without_the_panel() => Assert.Null(JeonbunFinder.FindAnySize(new Image(1280, 720)));

    // Is the in-game meter at its saved spot (no digits read): its green first cell, in the first row or (before the first hit,
    // under the "이전 기록을 선택하여…" line) the second. Gates the dungeon read (meter-gating.plan.md).
    [Theory]
    [InlineData("jeonbun_prefight")]   // 1막 at 00:00: dashes only
    [InlineData("jeonbun_0")]
    [InlineData("jeonbun_15")]         // support view, a flash behind it
    [InlineData("jeonbun_22")]         // two rows, mine second
    public void The_meter_is_present_at_its_spot(string fixture) =>
        Assert.True(JeonbunFinder.Present(Fixture.Load(fixture).Frames[0], 1.0));

    [Fact]
    public void Before_the_first_hit_the_meter_is_present_and_reads_empty()
    {
        var panel = Fixture.Load("jeonbun_prefight").Frames[0];
        Assert.True(JeonbunFinder.Present(panel, 1.0));
        Assert.Equal(0, JeonbunReader.Read(panel)!.Timer);   // 00:00, no damage: the meter's own read sees it too
    }

    [Fact]
    public void Hell_floors_show_no_meter_at_the_spot() =>
        Assert.False(JeonbunFinder.Present(Fixture.Load("jeonbun_hell").Frames[0], 1.0));

    [Fact]
    public void A_blank_spot_has_no_meter() => Assert.False(JeonbunFinder.Present(new Image(470, 114), 1.0));
}

