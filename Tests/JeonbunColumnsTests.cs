using System.Diagnostics;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The 전분 with the player's own columns (jeonbun-columns.plan.md): the 2026-11-18 patch made every column
/// 139 px at 1080p for 117 and the panel 559 wide for 470, and the player picks the columns (헤드어택 비중, 시너지B 유효율 here). It grows left from the
/// same right edge; the timer and row height stay. jeonbun_wide_N: the user's raid saves (1080p, crop 559×114 at 1359,846).
/// The other layouts are made from them here: columns swapped, one cut out, scaled to 1440p and 4K, a 21:9 screen.</summary>
public sealed class JeonbunColumnsTests
{
    const int Pitch = 139, FirstCell = 2, TitleTop = 33;   // the wide crops' cell pitch and first cell's left, the title line's top
    const int RowTop = 63, RowBottom = 87;                  // my row's cells in jeonbun_wide_0

    static Image Wide(int i) => Fixture.Load($"jeonbun_wide_{i}").Frames[0];

    // Phase 4: the scale comes from the spot's height (the panel keeps it whatever its columns), not the crop's width.
    [Theory]
    [InlineData(1359, 846, 559, 114, 1080, 1.0)]              // a found wide spot
    [InlineData(1448, 848, 470, 114, 1080, 1.0)]              // an old 470 save
    [InlineData(1812, 1128, 745, 152, 1440, 152 / 114.0)]     // a wide spot at 1440p
    [InlineData(1812, 1340, 745, 100, 1440, 1440 / 1080.0)]   // cut short at the game area's bottom: the screen's height
    [InlineData(1000, 928, 745, 152, 1080, 152 / 114.0)]      // whole at the bottom (a zoomed video): its own height
    [InlineData(1359, 846, 559, 0, 1080, 1.0)]                // no height (a hand-edited save): the screen's height
    public void The_panel_scale_comes_from_the_spot_height(int x, int y, int w, int h, int screenH, double scale) =>
        Assert.Equal(scale, Geometry.PanelScale(new PixelRect(x, y, w, h), new Viewport(0, 0, screenH * 16 / 9, screenH)), 3);

    [Fact]
    public void A_wide_box_keeps_its_shape_when_resized_many_times()
    {
        var a = FitAdjust.Begin(new Viewport(0, 0, 1920, 1080), 100, new Fit(100), panel: new PixelRect(1300, 846, 559, 114));
        for (int i = 0; i < 40; i++) a = a.Resize(AdjustPart.전분, 1);

        Assert.Equal(599, a.Panel.Width);
        Assert.Equal(122, a.Panel.Height);   // 599 · 114 / 559: the start's shape, not one step's rounding after another
    }

    [Fact]
    public void The_reading_grid_of_a_wide_box_ends_at_its_right_edge_and_scales_by_height()
    {
        var box = new PixelRect(1000, 500, 745, 152);   // a 559 panel at 1440p
        var lines = ReadingGrid.Of("g-jeonbun", new Viewport(0, 0, 2560, 1440), new Fit(100), 100, Geometry.BuffBoxCount, 100, box);

        Assert.Equal(box.Right - 3, lines.Max(l => Math.Max(l.X1, l.X2)));   // the last cell's right, 2 px in at 1080p
        Assert.Equal(box.X + 3, lines.Min(l => Math.Min(l.X1, l.X2)));
    }

    [Fact]
    public void A_wide_box_reads_green_in_a_picture()
    {
        var panel = Wide(0);
        var at = new PixelRect(1359, 846, panel.Width, panel.Height);

        Assert.True(PictureReads.Panel(panel, 1.0));
        Assert.True(PictureReads.Of(AdjustPart.전분, panel, at, new Viewport(0, 0, 1920, 1080), new Fit(100), 100, Geometry.BuffBoxCount, 100, at));
    }

    [Fact]
    public void A_wide_panel_reads_reinforced_at_its_scale() =>
        Assert.Equal(Saves[0].Reading, new JeonbunSoftReader().Read(Wide(0), 1.0, TimeSpan.FromSeconds(8)));

    // Each save's timer and my row's cells as the game shows them. wide_1 (mine second) was saved while the rows re-sorted: the
    // saved record's row sits half hidden behind mine and its 535.33억 and 44.61억 run into my first two cells, so those don't
    // read whole there (nor did they before); its other cells and its timer do.
    static readonly (string Timer, string?[] Cells, JeonbunReading? Reading)[] Saves =
    [
        ("00:08", ["5.39억", "6,745.71만", "11.51%", "99.38%"], new(539_000_000, 67_457_100, 8, false)),
        ("00:12", [null, null, "98.92%", "97.18%"], null),
        ("00:09", ["337.78억", "37.53억", "98.67%", "94.42%"], new(33_778_000_000, 3_753_000_000, 9, false)),
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]   // two rows, mine second
    [InlineData(2)]   // two rows, mine first
    public void A_wide_panel_reads_its_timer_and_cells(int i)
    {
        Assert.Equal(Saves[i].Timer, JeonbunReader.TimerText(Wide(i), 1.0));
        Assert.Equal(Saves[i].Cells, JeonbunReader.Cells(Wide(i), 1.0));
        Assert.Equal(Saves[i].Reading, JeonbunReader.Read(Wide(i), 1.0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_wide_panels_columns_are_known_by_their_titles(int i) =>
        Assert.Equal([JeonbunColumn.피해량, JeonbunColumn.DPS, JeonbunColumn.기타, JeonbunColumn.기타], JeonbunReader.Columns(Wide(i), 1.0));

    // The layout measured on each save: four columns of 139 between 2 and 557.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_wide_panels_layout_is_measured(int i)
    {
        var layout = JeonbunLayout.Measure(Wide(i), 1.0, i == 1 ? 1 : 0)!;
        Assert.Equal(4, layout.Count);
        Assert.InRange(layout.Edges[1], 139, 141);
        Assert.InRange(layout.Edges[^1], 556, 558);
    }

    // DPS first: the first two columns swapped, each cell's fill repainted to its place's colour (the colour follows the place,
    // decision 2: the first is green whatever it holds).
    [Fact]
    public void Columns_in_another_order_read_the_same()
    {
        var panel = Swapped(Wide(0));
        Assert.Equal([JeonbunColumn.DPS, JeonbunColumn.피해량, JeonbunColumn.기타, JeonbunColumn.기타], JeonbunReader.Columns(panel, 1.0));
        Assert.Equal(Saves[0].Reading, JeonbunReader.Read(panel, 1.0));
    }

    // Three columns: the third (헤드어택 비중) cut out, the panel 139 px narrower, the timer still at its right.
    [Fact]
    public void Three_columns_read_the_same()
    {
        var panel = WithoutThirdColumn(Wide(0));
        Assert.Equal([JeonbunColumn.피해량, JeonbunColumn.DPS, JeonbunColumn.기타], JeonbunReader.Columns(panel, 1.0));
        Assert.Equal(Saves[0].Reading, JeonbunReader.Read(panel, 1.0));
    }

    [Theory]
    [InlineData(4.0 / 3)]   // 1440p
    [InlineData(2.0)]       // 4K
    public void A_wide_panel_reads_at_other_resolutions(double f)
    {
        var w = Wide(0);
        Assert.Equal(Saves[0].Reading, JeonbunReader.Read(ImageOps.Resample(w, new PixelRect(0, 0, w.Width, w.Height), f), f));
    }

    // Any column width (decision: size-proof, user 2026-11-18): every column 30 or 60 px wider than the save's 139, at 1080p and
    // 4K. The old 117 px crops (JeonbunReaderTests) are the narrower case.
    [Theory]
    [InlineData(30, 1.0)]
    [InlineData(60, 1.0)]
    [InlineData(30, 2.0)]
    public void Columns_of_any_width_read_the_same(int extra, double f)
    {
        var w = Widened(Wide(0), extra);
        var panel = f == 1.0 ? w : ImageOps.Resample(w, new PixelRect(0, 0, w.Width, w.Height), f);
        Assert.Equal([JeonbunColumn.피해량, JeonbunColumn.DPS, JeonbunColumn.기타, JeonbunColumn.기타], JeonbunReader.Columns(panel, f));
        Assert.Equal(Saves[0].Reading, JeonbunReader.Read(panel, f));
    }

    // The old 470-wide crops read as before with the scale given (it is their width's): the change keeps them.
    [Fact]
    public void An_old_panel_reads_the_same_with_the_scale_given()
    {
        var old = Fixture.Load("jeonbun_0").Frames[0];
        Assert.Equal(JeonbunReader.Read(old), JeonbunReader.Read(old, 1.0));
    }

    // A line between cells lost under a flash: the columns are still measured from the others, the missing edge put back.
    [Fact]
    public void A_missing_line_is_put_back_between_its_neighbours()
    {
        var panel = Wide(0).Copy();
        for (int y = RowTop - 2; y < RowBottom + 2; y++)
            for (int x = FirstCell + Pitch - 4; x <= FirstCell + Pitch + 4; x++) SetPixel(panel, x, y, panel.Pixel(FirstCell + Pitch - 8, y));
        var layout = JeonbunLayout.Measure(panel, 1.0, 0)!;
        Assert.Equal(4, layout.Count);
        Assert.InRange(layout.Edges[1], 139, 142);
        Assert.Equal(Saves[0].Reading, JeonbunReader.Read(panel, 1.0));
    }

    // A dark speck in a cell's strips: one near the last line is skipped; one further on doesn't fit the columns, so the
    // default four stand in. Neither halves a column.
    [Theory]
    [InlineData(FirstCell + 70)]
    [InlineData(FirstCell + Pitch + 110)]
    public void A_speck_in_a_cell_is_no_line(int x)
    {
        var panel = Wide(0).Copy();
        foreach (int y in (int[])[RowTop, RowTop + 1, RowTop + 2, RowBottom - 3, RowBottom - 2, RowBottom - 1]) SetPixel(panel, x, y, (0, 0, 0));
        var layout = JeonbunLayout.Measure(panel, 1.0, 0)!;
        Assert.Equal(4, layout.Count);
        Assert.InRange(layout.Edges[1], 139, 142);
        Assert.Equal(Saves[0].Reading, JeonbunReader.Read(panel, 1.0));
    }

    // A crop that ends inside the last column (a spot saved before the patch, 470 wide, on the wider panel): no layout, no read.
    [Fact]
    public void A_crop_that_cuts_the_panel_reads_nothing()
    {
        var w = Wide(0);
        var cut = ImageOps.Crop(w, new PixelRect(0, 0, w.Width - 50, w.Height));
        Assert.Null(JeonbunLayout.Measure(cut, 1.0, 0));
        Assert.Null(JeonbunReader.Read(cut, 1.0));
    }

    // The layout is measured once per spot (plan: under 0.2 ms at 1080p): 0.12 ms on wide_0 (2026-11-18, best of 5); 0.5 leaves
    // room for a loaded machine. The whole read is 10.0 ms there, 9.3 on today's 470 (main: 9.1).
    [Fact]
    public void A_layout_is_measured_in_a_fraction_of_a_millisecond()
    {
        var panel = Wide(0);
        JeonbunLayout.Measure(panel, 1.0, 0);
        double best = double.MaxValue;   // the best of 5 runs: a loaded machine slows some, not all
        for (int run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) JeonbunLayout.Measure(panel, 1.0, 0);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / 20);
        }
        Assert.True(best < 0.5, $"{best:0.000} ms");
    }

    // --- the search

    [Fact]
    public void A_wide_panel_is_found_at_its_real_size_in_the_game()
    {
        var found = JeonbunFinder.Find(Fixture.Load("jeonbun_wide_scene").Frames[0], 1.0);
        AssertRect(found, 139, 66, 559, 1.0);
    }

    [Theory]
    [InlineData(0, 700, 600)]
    [InlineData(2, 500, 40)]    // two rows, mine first
    public void Each_wide_panel_is_found_where_it_is(int i, int x, int y) =>
        AssertRect(JeonbunFinder.Find(Screen(Wide(i), 1280, 720, x, y), 1.0), x, y, 559, 1.0);

    // wide_1 (mine second) doesn't read (its damage and DPS run into the saved row, Saves), and a spot counts only when it reads.
    [Fact]
    public void A_wide_panel_that_does_not_read_is_not_found() =>
        Assert.Null(JeonbunFinder.Find(Screen(Wide(1), 1280, 720, 120, 200), 1.0));

    // A dark line in the scene a column or two past the panel's last cell (a pole, a wall's edge) is not taken for the panel's.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_dark_line_past_the_panel_does_not_hide_it(int columns)
    {
        var screen = Screen(Wide(0), 1280, 720, 300, 300);
        int x = 300 + 557 + columns * 139;
        for (int y = 0; y < screen.Height; y++)
            for (int c = 0; c < 3; c++) screen.Bgra[y * screen.Stride + x * 4 + c] = 0;
        AssertRect(JeonbunFinder.Find(screen, 1.0), 300, 300, 559, 1.0);
    }

    [Fact]
    public void A_reordered_panel_is_found() =>
        AssertRect(JeonbunFinder.Find(Screen(Swapped(Wide(0)), 1280, 720, 300, 300), 1.0), 300, 300, 559, 1.0);

    [Fact]
    public void A_three_column_panel_is_found() =>
        AssertRect(JeonbunFinder.Find(Screen(WithoutThirdColumn(Wide(0)), 1280, 720, 300, 300), 1.0), 300, 300, 420, 1.0);

    [Theory]
    [InlineData(4.0 / 3, 2560, 1440)]
    [InlineData(2.0, 3840, 2160)]
    public void A_wide_panel_is_found_at_other_resolutions(double f, int w, int h)
    {
        var p = Wide(0);
        var panel = ImageOps.Resample(p, new PixelRect(0, 0, p.Width, p.Height), f);
        int x = w - panel.Width - (int)(2 * f), y = (int)(846 * f);
        AssertRect(JeonbunFinder.Find(Screen(panel, w, h, x, y), f), x, y, 559 * f, f);
    }

    // 21:9 (2560×1080): the panel at the game area's bottom right, black bars none here (the game fills the screen).
    [Fact]
    public void A_wide_panel_is_found_on_a_21_9_screen() =>
        AssertRect(JeonbunFinder.Find(Screen(Wide(0), 2560, 1080, 2560 - 561, 846), 1.0), 2560 - 561, 846, 559, 1.0);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_wide_panel_is_present_at_its_spot(int i) => Assert.True(JeonbunFinder.Present(Wide(i), 1.0));

    // belgardin-party run 1 (2026-11-18) took a 559 spot ending at the primary screen's right edge, not reproduced since: a
    // green shape cut by the edge with no panel around it is not a spot, and a panel cut by the edge is not taken past it.
    [Theory]
    [InlineData(112)]
    [InlineData(300)]
    public void A_green_block_at_the_screen_edge_alone_is_not_found(int width)
    {
        var screen = Screen(new Image(1, 1), 1920, 1080, 0, 0);
        for (int y = 896 + 63; y < 896 + 63 + 23; y++)
            for (int x = 1920 - width; x < 1920; x++) SetPixel(screen, x, y, (60, 170, 60));
        Assert.Null(JeonbunFinder.Find(screen, 1.0));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(200)]
    public void A_panel_cut_by_the_screen_edge_is_not_taken_past_it(int cut)
    {
        var panel = Wide(0);
        int x = 1920 - panel.Width + cut;
        var screen = Screen(new Image(1, 1), 1920, 1080, 0, 0);
        for (int py = 0; py < panel.Height; py++)
            panel.Row(py)[..((1920 - x) * 4)].CopyTo(screen.Bgra.AsSpan((846 + py) * screen.Stride + x * 4));
        var found = JeonbunFinder.Find(screen, 1.0);
        Assert.True(found is null || found.Value.Right <= 1920, $"{found}");
    }

    static void AssertRect(PixelRect? found, int x, int y, double width, double scale)
    {
        Assert.NotNull(found);
        var r = found!.Value;
        Assert.InRange(r.X, x - 2, x + 2);
        Assert.InRange(r.Y, y - 2, y + 2);
        Assert.InRange(r.Width, width - 3 * scale, width + 3 * scale);
        Assert.InRange(r.Height, 88 * scale - 1, 114 * scale + 1);
    }

    /// <summary>A dark screen with <paramref name="panel"/> pasted at (x, y).</summary>
    static Image Screen(Image panel, int w, int h, int x, int y)
    {
        var s = new Image(w, h);
        for (int i = 0; i < s.Bgra.Length; i += 4) (s.Bgra[i], s.Bgra[i + 1], s.Bgra[i + 2], s.Bgra[i + 3]) = (40, 32, 30, 255);
        for (int py = 0; py < panel.Height; py++)
            panel.Row(py).CopyTo(s.Bgra.AsSpan((y + py) * s.Stride + x * 4, panel.Width * 4));
        return s;
    }

    /// <summary>The first two columns swapped (titles and cells, below the timer's line), each of my row's cells repainted from
    /// its fill to the other's.</summary>
    static Image Swapped(Image p)
    {
        var dst = p.Copy();
        int a = FirstCell, b = FirstCell + Pitch;
        for (int y = TitleTop; y < p.Height; y++)
            for (int x = 0; x < Pitch; x++)
            {
                SetPixel(dst, a + x, y, p.Pixel(b + x, y));
                SetPixel(dst, b + x, y, p.Pixel(a + x, y));
            }
        var green = Fill(p, a);
        var brown = Fill(p, b);
        Repaint(dst, a, brown, green);
        Repaint(dst, b, green, brown);
        return dst;
    }

    /// <summary>Every column <paramref name="extra"/> px wider (even): a text-free strip 6 px in from each cell's sides repeated
    /// <paramref name="extra"/>/2 times; the timer's line above keeps the right edge.</summary>
    static Image Widened(Image p, int extra)
    {
        var dst = new Image(p.Width + 4 * extra, p.Height);
        var from = new List<int>();   // the source x of each of dst's x, from the title line down
        for (int x = 0; x < FirstCell; x++) from.Add(x);
        for (int left = FirstCell; left < FirstCell + 4 * Pitch; left += Pitch)
            for (int x = left; x < left + Pitch; x++)
            {
                from.Add(x);
                if (x == left + 6 || x == left + Pitch - 6) from.AddRange(Enumerable.Repeat(x, extra / 2));
            }
        for (int x = FirstCell + 4 * Pitch; x < p.Width; x++) from.Add(x);
        for (int y = 0; y < p.Height; y++)
            for (int x = 0; x < dst.Width; x++)
                SetPixel(dst, x, y, y < TitleTop ? p.Pixel(Math.Max(0, x - 4 * extra), y) : p.Pixel(from[x], y));
        return dst;
    }

    /// <summary>The third column cut out: the timer's line keeps the right edge, the columns below close up.</summary>
    static Image WithoutThirdColumn(Image p)
    {
        var dst = new Image(p.Width - Pitch, p.Height);
        int cut = FirstCell + 2 * Pitch;
        for (int y = 0; y < p.Height; y++)
            for (int x = 0; x < dst.Width; x++)
                SetPixel(dst, x, y, y < TitleTop ? p.Pixel(x + Pitch, y) : p.Pixel(x < cut ? x : x + Pitch, y));
        return dst;
    }

    /// <summary>A cell's fill in my row: the median of its pixels' channels, which the text barely touches.</summary>
    static (byte B, byte G, byte R) Fill(Image p, int left)
    {
        var px = new List<(byte B, byte G, byte R)>();
        for (int y = RowTop + 2; y < RowBottom - 2; y++)
            for (int x = left + 3; x < left + Pitch - 4; x++) px.Add(p.Pixel(x, y));
        byte Mid(Func<(byte B, byte G, byte R), byte> c) => px.Select(c).Order().ElementAt(px.Count / 2);
        return (Mid(c => c.B), Mid(c => c.G), Mid(c => c.R));
    }

    /// <summary>My row's cell at <paramref name="left"/> repainted: white text over fill <paramref name="from"/> becomes the same
    /// text over <paramref name="to"/> (each pixel's text share from how far it rose toward white).</summary>
    static void Repaint(Image img, int left, (byte B, byte G, byte R) from, (byte B, byte G, byte R) to)
    {
        for (int y = RowTop; y < RowBottom; y++)
            for (int x = left; x < left + Pitch - 1; x++)
            {
                var (b, g, r) = img.Pixel(x, y);
                double Share(byte v, byte f) => f >= 250 ? 0 : Math.Clamp((v - f) / (255.0 - f), 0, 1);
                double t = (Share(b, from.B) + Share(g, from.G) + Share(r, from.R)) / 3;
                byte Mix(byte f) => (byte)Math.Round(t * 255 + (1 - t) * f);
                SetPixel(img, x, y, (Mix(to.B), Mix(to.G), Mix(to.R)));
            }
    }

    static void SetPixel(Image img, int x, int y, (byte B, byte G, byte R) c)
    {
        int i = y * img.Stride + x * 4;
        (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (c.B, c.G, c.R, 255);
    }
}
