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
}
