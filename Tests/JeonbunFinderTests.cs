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

