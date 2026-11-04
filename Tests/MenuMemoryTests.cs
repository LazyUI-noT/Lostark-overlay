using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The game menu read from its fixed places: a card's name strip seen once is known by its pixels, no OCR; thresholds
/// measured on goal.png, a live 1080p capture and homework_tracker.mp4 (same strip ≤ 2.1, other strips ≥ 6.7; title open ≤ 3.2, closed ≥ 25).</summary>
public class MenuMemoryTests
{
    static readonly Viewport Hd = new(0, 0, 1920, 1080);
    static readonly PixelRect Whole = new(0, 0, 1920, 1080);

    static byte[] Strip(byte shade, byte text = 200)
    {
        var s = new byte[MenuSlots.StripWidth * MenuSlots.StripHeight];
        Array.Fill(s, shade);
        for (int i = 0; i < s.Length; i += 7) s[i] = text;
        return s;
    }

    static MenuCard Card(string name) => new(name, "워로드", 0, 0, 245, 19, 1771);

    static MenuCard At(string name, double x, double y) => new(name, "워로드", x, y, 245, y + 19, 1771);

    [Fact]
    public void The_card_under_the_mouse_keeps_its_place_while_the_others_stay()
    {
        var (cards, playing) = MenuSlots.Steady(([At("a", 846, 426), At("b", 1106, 426)], At("me", 586, 426)), ([At("a", 846, 426)], null));
        Assert.Equal(["a", "b"], cards.Select(c => c.Name));
        Assert.Equal("me", playing!.Name);
    }

    [Fact]
    public void A_scrolled_grid_shows_only_what_is_known_now()
    {
        var (cards, playing) = MenuSlots.Steady(([At("a", 846, 426), At("b", 1106, 426)], At("me", 586, 426)), ([At("b", 846, 426)], null));
        Assert.Equal(["b"], cards.Select(c => c.Name));
        Assert.Null(playing);
    }

    [Fact]
    public void Nothing_known_shows_nothing()
    {
        var (cards, playing) = MenuSlots.Steady(([At("a", 846, 426)], At("me", 586, 426)), ([], null));
        Assert.Empty(cards);
        Assert.Null(playing);
    }

    [Fact]
    public void A_new_card_replaces_what_was_shown() =>
        Assert.Equal(["new"], MenuSlots.Steady(([At("a", 846, 426)], null), ([At("new", 1106, 426)], null)).Cards.Select(c => c.Name));

    /// <summary>A screen with text-like marks in a slot's name strip, the same wherever the slot is.</summary>
    static Image Screen(params (int Col, int Row, byte Shade)[] marks)
    {
        var img = new Image(1920, 1080);
        foreach (var (col, row, shade) in marks)
        {
            var r = MenuSlots.RefStrip(col, row);
            for (int y = r.Y; y < r.Bottom; y++)
                for (int x = r.X; x < r.Right; x++)
                {
                    int i = (y * 1920 + x) * 4;
                    byte v = (x - r.X + y - r.Y) % 5 == 0 ? (byte)230 : shade;
                    (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (v, v, v, 255);
                }
        }
        return img;
    }

    [Fact]
    public void A_strip_seen_again_is_known_and_another_is_not()
    {
        var memory = new MenuMemory();
        memory.Learn(Strip(40), Card("매우"), playing: false);

        Assert.Equal("매우", memory.Recall(Strip(42))?.Card.Name);   // within the capture noise
        Assert.Null(memory.Recall(Strip(60)));
    }

    [Fact]
    public void A_new_read_of_the_same_strip_replaces_the_old_one()
    {
        var memory = new MenuMemory();
        memory.Learn(Strip(40), Card("매우Ot"), playing: false);
        memory.Learn(Strip(41), Card("매우"), playing: false);

        Assert.Equal("매우", memory.Recall(Strip(40))?.Card.Name);
    }

    [Fact]
    public void The_memory_keeps_only_so_many_strips_dropping_the_oldest()
    {
        var memory = new MenuMemory();
        for (int i = 0; i <= MenuMemory.MaxStrips; i++) memory.Learn(Strip((byte)(i * 3 % 250), (byte)(i * 11 % 250)), Card($"c{i}"), playing: false);

        Assert.Equal(MenuMemory.MaxStrips, memory.Count);
        Assert.Equal($"c{MenuMemory.MaxStrips}", memory.Recall(Strip((byte)(MenuMemory.MaxStrips * 3 % 250), (byte)(MenuMemory.MaxStrips * 11 % 250)))?.Card.Name);
    }

    [Fact]
    public void The_title_is_compared_once_learned()
    {
        var memory = new MenuMemory();
        Assert.False(memory.TitleKnown);

        memory.LearnTitle(Strip(30));
        Assert.True(memory.TitleKnown);
        Assert.True(memory.IsTitle(Strip(33)));
        Assert.False(memory.IsTitle(Strip(90)));
    }

    [Fact]
    public void Clearing_forgets_the_strips_and_the_title()
    {
        var memory = new MenuMemory();
        memory.Learn(Strip(40), Card("매우"), playing: false);
        memory.LearnTitle(Strip(30));

        memory.Clear();

        Assert.Null(memory.Recall(Strip(40)));
        Assert.False(memory.TitleKnown);
    }

    [Fact]
    public void A_card_goes_to_its_slot_whatever_row_the_scroll_put_it_in()
    {
        var memory = new MenuMemory();
        var read = new MenuRead(true, null, null, [new MenuCard("매우", "워로드", 846, 426, 245, 445, 1771)], false);
        MenuSlots.Learn(memory, read, MenuSlots.Strips(Screen((1, 0, 40)), Whole, Hd));

        var (cards, playing) = MenuSlots.Recalled(memory, MenuSlots.Strips(Screen((0, 2, 40)), Whole, Hd));

        var card = Assert.Single(cards);
        Assert.Equal(("매우", 586d, 660d, 679d), (card.Name, card.X, card.Y, card.Bottom));
        Assert.Equal(1771, card.ItemLevel);
        Assert.Null(playing);
    }

    [Fact]
    public void The_played_card_is_remembered_as_played()
    {
        var memory = new MenuMemory();
        var read = new MenuRead(true, "나", "나", [], false, null, new MenuCard("", null, 586, 426, 245, 445));
        MenuSlots.Learn(memory, read, MenuSlots.Strips(Screen((0, 0, 40)), Whole, Hd));

        var (cards, playing) = MenuSlots.Recalled(memory, MenuSlots.Strips(Screen((0, 0, 40)), Whole, Hd));

        Assert.Empty(cards);
        Assert.Equal((586d, 426d), (playing?.X, playing?.Y));
    }

    [Fact]
    public void An_empty_slot_is_never_the_played_card()
    {
        var memory = new MenuMemory();
        var strips = MenuSlots.Strips(Screen(), Whole, Hd);   // nothing drawn: the short last row's gap, guessed to be the played card
        var read = new MenuRead(true, "나", "나", [], false, null, new MenuCard("", null, 1106, 660, 245, 679));

        MenuSlots.Learn(memory, read, strips);
        var merged = MenuSlots.WithRecalled(read, memory, strips);

        Assert.Equal(0, memory.Count);
        Assert.Null(merged.PlayingCard);
    }

    [Fact]
    public void A_read_keeps_what_the_memory_knows_when_the_ocr_misses_a_card()
    {
        var memory = new MenuMemory();
        var strips = MenuSlots.Strips(Screen((1, 0, 40), (2, 0, 90)), Whole, Hd);
        MenuSlots.Learn(memory, new MenuRead(true, null, null, [new MenuCard("매우", null, 846, 426, 245, 445), new MenuCard("김뚜", null, 1106, 426, 245, 445)], false), strips);
        var partial = new MenuRead(true, "나", "나", [new MenuCard("김뚜", null, 1106, 426, 245, 445)], false);

        var merged = MenuSlots.WithRecalled(partial, memory, strips);

        Assert.Equal(["매우", "김뚜"], merged.Cards.Select(c => c.Name));
        Assert.Equal("나", merged.Current);
    }

    [Fact]
    public void The_slots_follow_the_screen_height_and_centre()
    {
        var wide = new Viewport(0, 0, 3440, 1440);   // 21:9: the menu centred at the game area's height
        var r = Geometry.MenuArea(wide, MenuSlots.RefStrip(0, 0));
        Assert.Equal(((3440 - 2560) / 2 + (586 + 50) * 4 / 3, (426 - 24) * 4 / 3), (r.X, r.Y));
    }

    [Fact]
    public void A_change_in_the_grid_is_a_move_and_one_in_the_right_list_is_not()
    {
        var (w, h) = (MenuSlots.RefWatch.Width, MenuSlots.RefWatch.Height);
        var still = MenuSlots.Cells(new Image(w, h), 1);
        var grid = new Image(w, h);
        for (int y = 100; y < 130; y++) for (int x = 100; x < 130; x++) grid.Bgra[(y * w + x) * 4 + 1] = 255;
        var list = new Image(w, h);
        for (int y = 200; y < 230; y++) for (int x = 900; x < 930; x++) list.Bgra[(y * w + x) * 4 + 1] = 255;

        Assert.True(MenuSlots.Moved(still, MenuSlots.Cells(grid, 1)));
        Assert.False(MenuSlots.Moved(still, MenuSlots.Cells(list, 1)));
        Assert.False(MenuSlots.Moved(still, still));
    }

    static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Opening_reads_at_once_and_once_more()
    {
        var pace = new MenuPace();
        pace.Open(Ms(0));

        Assert.True(pace.Due(Ms(0)));
        pace.Read(Ms(0));
        Assert.False(pace.Due(Ms(100)));
        Assert.True(pace.Due(Ms(500)));
        pace.Read(Ms(500));
        Assert.False(pace.Due(Ms(1000)));   // quiet now
    }

    [Fact]
    public void A_still_menu_is_read_only_every_few_seconds()
    {
        var pace = new MenuPace();
        pace.Open(Ms(0));
        pace.Read(Ms(0));
        pace.Read(Ms(500));

        Assert.False(pace.Due(Ms(500) + MenuPace.Quiet - Ms(1)));
        Assert.True(pace.Due(Ms(500) + MenuPace.Quiet));
    }

    [Fact]
    public void A_change_waits_to_settle_then_reads_twice()
    {
        var pace = new MenuPace();
        pace.Open(Ms(0));
        pace.Read(Ms(0));
        pace.Read(Ms(500));

        pace.Changed(Ms(2000));
        Assert.False(pace.Due(Ms(2000)));
        Assert.True(pace.Due(Ms(2000) + MenuPace.Settle));
        pace.Read(Ms(2200));
        Assert.True(pace.Due(Ms(2200) + MenuPace.MinGap));
    }

    [Fact]
    public void A_screen_that_never_settles_is_read_once_a_second_at_most()
    {
        var pace = new MenuPace();
        pace.Open(Ms(0));
        pace.Read(Ms(0));

        for (int t = 50; t < 1050; t += 50)
        {
            pace.Changed(Ms(t));
            Assert.False(pace.Due(Ms(t)));
        }
        pace.Changed(Ms(1050));
        Assert.True(pace.Due(Ms(1050)));
    }
}
