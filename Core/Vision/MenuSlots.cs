using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The game menu's fixed places (user, 2026-11-04: the window doesn't move, a scroll lands on the same grid), at 1080p in
/// the 16:9 frame, measured on goal.png and a live capture; <see cref="Geometry.MenuArea"/> puts them on any screen (they follow the
/// game area's height and centre only). Pure.</summary>
public static class MenuSlots
{
    public const int Columns = 3, Rows = 3, StripWidth = 190, StripHeight = 22;
    const int CardHeight = 19, StripRight = 50, StripAbove = 24, SlotSnap = 20;
    const int CellSize = 16, CellStep = 2, MovedLevels = 3;
    const int BlankContrast = 30;   // a strip with text spans far more; the played card's dimmed name and "접속 중" too
    static readonly int[] Xs = [586, 846, 1106], Ys = [426, 543, 660];

    /// <summary>"게임 메뉴" itself, inside <see cref="Geometry.RefMenuTitle"/>.</summary>
    public static readonly PixelRect RefTitleWord = new(1040, 258, 90, 26);
    /// <summary>What is watched for a change: the grid, the picked name above 전체 현황 보기 and the buttons (not the portrait, which moves).</summary>
    public static readonly PixelRect RefWatch = new(586, 300, 1174, 470);
    /// <summary>The right list (x 1380+, y 370+) is skipped: its icons may shine, and nothing is read from it.</summary>
    static readonly PixelRect RefSkipped = new(1380, 370, 380, 400);

    /// <summary>A slot's name strip.</summary>
    public static PixelRect RefStrip(int col, int row) => new(Xs[col] + StripRight, Ys[row] - StripAbove, StripWidth, StripHeight);

    /// <summary>The grey pixels of <paramref name="rect"/> (screen pixels) in <paramref name="img"/>, which holds <paramref name="area"/>.</summary>
    public static byte[] Grey(Image img, PixelRect area, PixelRect rect)
    {
        var grey = new byte[rect.Width * rect.Height];
        for (int y = 0; y < rect.Height; y++)
        {
            int row = (rect.Y - area.Y + y) * img.Width + rect.X - area.X;
            for (int x = 0; x < rect.Width; x++)
            {
                int i = (row + x) * 4;
                grey[y * rect.Width + x] = (byte)((img.Bgra[i] * 29 + img.Bgra[i + 1] * 150 + img.Bgra[i + 2] * 77) >> 8);
            }
        }
        return grey;
    }

    /// <summary>Every slot's name strip, left to right and top to bottom, from an image holding them.</summary>
    public static byte[][] Strips(Image img, PixelRect area, Viewport vp)
    {
        var strips = new byte[Columns * Rows][];
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Columns; col++)
                strips[row * Columns + col] = Grey(img, area, Geometry.MenuArea(vp, RefStrip(col, row)));
        return strips;
    }

    /// <summary>What an OCR read found goes with its slot's strip; a card elsewhere is left out, and an empty slot is no card (the grid's
    /// gap guessed to be the played one).</summary>
    public static void Learn(MenuMemory memory, MenuRead read, byte[][] strips)
    {
        foreach (var card in read.Cards)
            if (SlotOf(card) is { } slot && !Blank(strips[slot])) memory.Learn(strips[slot], card, playing: false);
        if (read.PlayingCard is { } played && SlotOf(played) is { } at && !Blank(strips[at])) memory.Learn(strips[at], played, playing: true);
    }

    /// <summary>The cards known by their strips, each at the slot it is in now, and the played one.</summary>
    public static (IReadOnlyList<MenuCard> Cards, MenuCard? Playing) Recalled(MenuMemory memory, byte[][] strips)
    {
        var cards = new List<MenuCard>();
        MenuCard? playing = null;
        for (int i = 0; i < strips.Length; i++)
        {
            if (memory.Recall(strips[i]) is not { } known) continue;
            var placed = Placed(known.Card, i);
            if (known.Playing) playing = placed;
            else cards.Add(placed);
        }
        return (cards, playing);
    }

    /// <summary>What to show given what was shown: a card the mouse is over reads differently, so while every card known now is still in
    /// the slot it was in, the others stay. Any card elsewhere, or none known, means the grid moved: only what is known now.</summary>
    public static (IReadOnlyList<MenuCard> Cards, MenuCard? Playing) Steady((IReadOnlyList<MenuCard> Cards, MenuCard? Playing) shown, (IReadOnlyList<MenuCard> Cards, MenuCard? Playing) now)
    {
        static bool Same(MenuCard a, MenuCard b) => a.Name == b.Name && a.X == b.X && a.Y == b.Y;
        bool moved = (now.Cards.Count == 0 && now.Playing is null)
            || now.Cards.Any(c => !shown.Cards.Any(k => Same(k, c)))
            || (now.Playing is { } p && shown.Playing is { } was && !Same(p, was));
        if (moved) return now;
        return ([.. now.Cards.Concat(shown.Cards.Where(k => !now.Cards.Any(c => Same(c, k))))], now.Playing ?? shown.Playing);
    }

    /// <summary>A read with what the memory knows put in: a card the OCR missed this time stays, and an empty slot isn't the played card.</summary>
    public static MenuRead WithRecalled(MenuRead read, MenuMemory memory, byte[][] strips)
    {
        var (cards, playing) = Recalled(memory, strips);
        var added = read.Cards.Where(c => SlotOf(c) is { } slot && !Blank(strips[slot]) && !cards.Any(k => SlotOf(k) == slot));
        var guessed = read.PlayingCard is { } p && SlotOf(p) is { } at && !Blank(strips[at]) ? p : null;
        return read with
        {
            Cards = [.. cards.Concat(added).OrderBy(SlotOf)],
            PlayingCard = playing ?? guessed,
        };
    }

    /// <summary>The picture of <see cref="RefWatch"/> in coarse cells, to notice a change cheaply: thin text and borders still move a cell's mean.</summary>
    public static byte[] Cells(Image watch, double scale)
    {
        int size = Math.Max(1, (int)Math.Round(CellSize * scale)), step = Math.Max(1, (int)Math.Round(CellStep * scale));
        int columns = watch.Width / size, rows = watch.Height / size;
        var cells = new byte[columns * rows];
        int skipX = (int)((RefSkipped.X - RefWatch.X) * scale), skipY = (int)((RefSkipped.Y - RefWatch.Y) * scale);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                if (col * size + size > skipX && row * size + size > skipY) continue;
                int sum = 0, n = 0;
                for (int y = row * size; y < (row + 1) * size; y += step)
                    for (int x = col * size; x < (col + 1) * size; x += step)
                    {
                        int i = (y * watch.Width + x) * 4;
                        sum += (watch.Bgra[i] * 29 + watch.Bgra[i + 1] * 150 + watch.Bgra[i + 2] * 77) >> 8;
                        n++;
                    }
                cells[row * columns + col] = (byte)(sum / n);
            }
        return cells;
    }

    public static bool Moved(byte[] was, byte[] now)
    {
        if (was.Length != now.Length) return true;
        for (int i = 0; i < was.Length; i++)
            if (Math.Abs(was[i] - now[i]) >= MovedLevels) return true;
        return false;
    }

    /// <summary><paramref name="card"/> moved to <paramref name="slot"/>.</summary>
    public static MenuCard Placed(MenuCard card, int slot) =>
        card with { X = Xs[slot % Columns], Y = Ys[slot / Columns], Bottom = Ys[slot / Columns] + CardHeight };

    /// <summary>The slot a card is in (left to right, top to bottom), or null off the grid's places.</summary>
    public static int? SlotOf(MenuCard card)
    {
        int col = Array.FindIndex(Xs, x => Math.Abs(x - card.X) < SlotSnap), row = Array.FindIndex(Ys, y => Math.Abs(y - card.Y) < SlotSnap);
        return col < 0 || row < 0 ? null : row * Columns + col;
    }

    static bool Blank(byte[] strip) => strip.Length == 0 || strip.Max() - strip.Min() < BlankContrast;
}
