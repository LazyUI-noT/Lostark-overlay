using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Boss debuff rows put together from the game's own icons (user, 2026-11-01: "create an artificial simulation to test and
/// confirm"): amsu_live_120_slot3 holds, at 1080p with 보스 디버프 크기 set to 120, two debuffs (3초, 2초) and a 빛암 (22초) on a
/// 31 px spacing: the clips' 110% (their 120% is 34 px), so the game was likely at 110. Each cell (icon, frame and the "N초"
/// under it) is copied into a row of any length, so a bomb can sit in any slot, behind any number of debuffs, and move left as
/// they expire.</summary>
public sealed class AmsuSimulationTests
{
    static readonly Image Source = Fixture.Load("amsu_live_120_slot3").Frames[0];
    static readonly AmsuLayout Layout = AmsuLayout.Of(new Viewport(0, 0, 1920, 1080), new Fit(110.05), 120);
    const int First = 228, Spacing = 31, Line = 41;   // the first debuff's left frame line, the slot spacing, the row's line
    const int Debuff1 = 0, Debuff2 = 1, Bitam = 2;    // cells in the source: 3초, 2초, the 빛암 (22초)
    const int CellTop = 34, CellBottom = 96;          // icon and text rows of a cell

    /// <summary>The source strip with its debuff cells cleared and <paramref name="cells"/> pasted from the first slot on
    /// (null: an empty slot).</summary>
    static Image Row(params int?[] cells)
    {
        var row = new Image(Source.Width, Source.Height, (byte[])Source.Bgra.Clone());
        var background = BackgroundCell();
        for (int slot = 0; slot < 3; slot++) Paste(row, background, First + slot * Spacing);
        for (int slot = 0; slot < cells.Length; slot++)
            Paste(row, cells[slot] is { } c ? Cell(c) : background, First + slot * Spacing);
        return row;
    }

    static Image Cell(int index) => Source.Crop(new PixelRect(First + index * Spacing, CellTop, Spacing, CellBottom - CellTop));

    // The empty row right of the source's last icon: the scene behind the slots.
    static Image BackgroundCell() => Source.Crop(new PixelRect(First + 4 * Spacing, CellTop, Spacing, CellBottom - CellTop));

    static void Paste(Image row, Image cell, int x) => Paste(row, cell, x, CellTop);

    static int?[] BombBehind(int ahead) => [.. Enumerable.Range(0, ahead).Select(i => (int?)(i % 2 == 0 ? Debuff1 : Debuff2)), Bitam];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]   // the tenth slot: the most 120% shows
    public void A_bomb_behind_any_number_of_debuffs_is_found_in_its_slot_with_its_seconds(int ahead)
    {
        var row = Row(BombBehind(ahead));
        var walk = AmsuReader.Walk(row, Layout.Scale, Layout.Slots, Line);
        Assert.Equal(Enumerable.Range(0, ahead + 1), walk.Filled.Select(f => f.Slot));
        var bomb = Assert.Single(AmsuReader.Locate(row, Layout.Scale, Layout.Slots, Line).Bombs);
        Assert.Equal((Bomb.빛암, (int?)ahead), (bomb.Kind, bomb.Slot));
        Assert.Equal(22, AmsuReader.ReadSeconds(row, bomb, Layout.TextScale));
        Assert.Equal(ahead + 1 >= Layout.Slots.Most, walk.Full);
    }

    // Every 보스 디버프 크기 (user, 2026-11-01: "use the spacing in the videos to recreate the rows"): the icons' left-to-left spacing
    // measured on 빛암 80_90.mp4 and 빛암 120_110_100.mp4 (1080p, HUD 100): 22, 25, 28, 31, 34 px at 80–120%, the frames 2 px apart
    // (icons 20, 23, 26, 29, 32 px). The source cells (31 px apart, as at 110% in the clips) are resized to that spacing and put
    // on it, the first slot at screen x 928 and the icons' top at screen y 169 at every size, as in the clips. The slots and the
    // kind are checked here; the "N초" at each size on the game's own text (bitam_* in AmsuReaderTests): resized text is softer.
    static int MeasuredSpacing(int size) => 3 * size / 10 - 2;

    static readonly Viewport Hd = new(0, 0, 1920, 1080);
    static readonly Fit Hud = new(110.05);
    const int ScreenFirst = 700 + First, ScreenTop = 128 + Line;   // 928, 169

    static (Image Row, AmsuLayout Layout, int Line) RowAt(int size, int?[] cells)
    {
        var rect = Geometry.BossRowSearch(Hd, Hud, size);
        int spacing = MeasuredSpacing(size);
        double f = spacing / (double)Spacing;
        var row = new Image(rect.Width, rect.Height);
        var background = Shrunk(BackgroundCell(), f);
        for (int y = 0; y < row.Height; y += background.Height)
            for (int x = 0; x < row.Width; x += background.Width) Paste(row, background, x, y);
        int top = (int)Math.Round(ScreenTop - (Line - CellTop) * f) - rect.Y;
        for (int slot = 0; slot < cells.Length; slot++)
            Paste(row, Shrunk(cells[slot] is { } c ? Cell(c) : BackgroundCell(), f), ScreenFirst + slot * spacing - rect.X, top);
        return (row, AmsuLayout.Of(Hd, Hud, size), ScreenTop - rect.Y);
    }

    static Image Shrunk(Image cell, double f) => ImageOps.Resample(cell, new PixelRect(0, 0, cell.Width, cell.Height), f);

    static void Paste(Image row, Image cell, int x0, int y0)
    {
        for (int y = 0; y < cell.Height; y++)
            for (int x = 0; x < cell.Width; x++)
                if (x0 + x >= 0 && x0 + x < row.Width && y0 + y >= 0 && y0 + y < row.Height)
                    Array.Copy(cell.Bgra, y * cell.Stride + x * 4, row.Bgra, (y0 + y) * row.Stride + (x0 + x) * 4, 4);
    }

    public static TheoryData<int, int> EverySlotAtEverySize()
    {
        var data = new TheoryData<int, int>();
        foreach (int size in new[] { 120, 110, 100, 90, 80 })
            for (int ahead = 0; ahead < Geometry.MaxBossDebuffs(size); ahead++) data.Add(size, ahead);
        return data;
    }

    [Theory]
    [MemberData(nameof(EverySlotAtEverySize))]
    public void At_every_size_a_bomb_in_any_slot_is_found(int size, int ahead)
    {
        var (row, layout, line) = RowAt(size, BombBehind(ahead));
        var walk = AmsuReader.Walk(row, layout.Scale, layout.Slots, line);
        Assert.Equal(Enumerable.Range(0, ahead + 1), walk.Filled.Select(f => f.Slot));
        var bomb = Assert.Single(AmsuReader.Locate(row, layout.Scale, layout.Slots, line).Bombs);
        Assert.Equal((Bomb.빛암, (int?)ahead), (bomb.Kind, bomb.Slot));
        Assert.Equal(ahead + 1 >= layout.Slots.Most, walk.Full);
    }

    [Fact]
    public void A_bomb_after_an_empty_slot_is_still_found()   // one empty slot is passed (a popup over one frame)
    {
        var bomb = Assert.Single(AmsuReader.Locate(Row(Debuff1, null, Bitam), Layout.Scale, Layout.Slots, Line).Bombs);
        Assert.Equal((Bomb.빛암, (int?)2), (bomb.Kind, bomb.Slot));
    }

    [Fact]
    public void The_loop_follows_a_bomb_moving_left_as_the_debuffs_ahead_expire()
    {
        // The 빛암 thrown behind five debuffs; one expires every 0.6 s, the row shifting left, read at the engine's cadence.
        var w = new AmsuWatch();
        var seen = new List<(double At, int? Slot)>();
        for (double at = 0; at <= 4.0; at += 0.1)
        {
            int ahead = Math.Max(0, 5 - (int)(at / 0.6));
            var now = TimeSpan.FromSeconds(at);
            var (pass, bossText) = w.Due(now, Layout.Timed);
            if (pass == RowPass.None) continue;
            w.Read(Layout, pass, bossText ? 1_197_954_508_761 : null, Row(BombBehind(ahead)), now);
            if (at >= 0.5) seen.Add((at, w.State.Up.TryGetValue(Bomb.빛암, out var t) ? t.Slot : null));
        }
        Assert.All(seen, s => Assert.NotNull(s.Slot));   // up the whole time, never lost on a shift
        Assert.Equal(0, seen[^1].Slot);                   // and on the first slot at the end
        Assert.Equal(Bomb.빛암, Amsu.View(w.State, TimeSpan.FromSeconds(4)).Shown);
    }
}
