using System.Text.RegularExpressions;

namespace GaugeOverlay.Core.Domain;

/// <summary>An OCR word of the game menu, in 1080p reference pixels (Engine.Menu maps the capture back).</summary>
public sealed record MenuWord(string Text, double X, double Y, double W, double H)
{
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
}

/// <summary>A character card in the 다른 캐릭터 접속 grid: its name, class and where it is (1080p reference pixels; the 숙제
/// tags go under <see cref="Bottom"/>).</summary>
public sealed record MenuCard(string Name, string? ClassName, double X, double Y, double W, double Bottom, int? ItemLevel = null);

/// <summary>One read of the game menu: open or not, the character being played (under the portrait), the one picked in the
/// grid (the right panel's header), the cards seen, and "잠시만 기다려주세요." (접속하기 pressed).</summary>
public sealed record MenuRead(bool Open, string? Current, string? Picked, IReadOnlyList<MenuCard> Cards, bool Waiting, int? CurrentLevel = null, MenuCard? PlayingCard = null)
{
    public static readonly MenuRead Closed = new(false, null, null, [], false);
}

/// <summary>Reads the ESC 게임 메뉴 from its OCR (homework.plan.md, reference/homework_project): by the text and its order, so the
/// layout's exact pixels don't matter. Distances are 1080p reference pixels measured on goal.png. Pure.</summary>
public static partial class GameMenu
{
    const double CardWidth = 245, CardLeft = 8, CardBelow = 7;   // a card from its "Lv" word: 8 px left of it, 245 wide, 7 px below
    const double NameMin = 10, NameMax = 30, ClassMax = 50;   // a card's name and class centres above its "Lv" word's centre
    const double NameFrom = 15, NameTo = 245;   // their x from the "Lv" word's left (past the class icon)
    const double HeaderGap = 40;   // the picked name above "전체 현황 보기"
    const double PortraitLevelBelow = 40, SameLine = 8;   // the portrait's item level under its "Lv" word; a card's beside its own
    const int MinItemLevel = 1000, MaxItemLevel = 2000;
    const double CellSnap = 20;   // cards in one column or row, within this

    /// <summary>The title strip's OCR shows "게임 메뉴" (read "계임 매뉴" or "계임 에뉴" as often as not): the menu is open.</summary>
    public static bool IsTitle(IEnumerable<string> lines) => lines.Any(l => Title().IsMatch(Squash(l)));

    public static MenuRead Parse(IReadOnlyList<IReadOnlyList<MenuWord>> lines, IEnumerable<string> waitingLines)
    {
        if (!lines.Any(l => Squash(Text(l)) is var text && (Title().IsMatch(text) || text.Contains("전체현황")))) return MenuRead.Closed;   // either: a misread title would blink the tags
        return new MenuRead(true, Current(lines), Picked(lines), Cards(lines), waitingLines.Any(l => Waiting().IsMatch(Squash(l))), CurrentLevel(lines), PlayingCardOf(lines) ?? TheGap(Cards(lines)));
    }

    /// <summary>The item level in what the OCR read, whole part ("1771.66"; "1640.OO", "會|79|.66": the icon before it, 1 read as |,
    /// 0 as O); null without a believable one.</summary>
    public static int? ItemLevel(string text)
    {
        var digits = text.Replace('|', '1').Replace('l', '1').Replace('O', '0').Replace('o', '0');
        if (FourDigits().Match(digits) is { Success: true } m) return int.Parse(m.Value) is >= MinItemLevel and <= MaxItemLevel and var level ? level : null;
        return NoLeadingOne().Match(digits) is { Success: true } rest ? 1000 + int.Parse(rest.Groups[1].Value) : null;   // "쉬791.66": the icon swallowed the 1
    }

    /// <summary>The item level on the line under the portrait's "Lv.70 name".</summary>
    static int? CurrentLevel(IReadOnlyList<IReadOnlyList<MenuWord>> lines)
    {
        var words = lines.SelectMany(l => l).ToList();
        foreach (var line in lines)
            for (int i = 0; i + 1 < line.Count; i++)
                if (LevelWord().Match(line[i].Text) is { Success: true } m && m.Length == line[i].Text.Length && line[i] is var lv
                    && ItemLevel(string.Concat(words.Where(w => w.CenterY - lv.CenterY is > 15 and < PortraitLevelBelow && w.X >= lv.X - 10 && w.X < lv.X + NameTo).Select(w => w.Text))) is { } level)
                    return level;
        return null;
    }

    /// <summary>"Lv.70 너무아파요살살" under the portrait: a level word, then a name on its line (a card's name is above its level).</summary>
    static string? Current(IReadOnlyList<IReadOnlyList<MenuWord>> lines)
    {
        foreach (var line in lines)
            for (int i = 0; i < line.Count; i++)
                if (LevelWord().Match(line[i].Text) is { Success: true } m)
                {
                    string rest = line[i].Text[m.Length..] + string.Concat(line.Skip(i + 1).Select(w => w.Text));
                    if (CharacterName.Clean(rest) is { } name) return name;
                }
        return null;
    }

    /// <summary>The name in the right panel's header, just above "전체 현황 보기".</summary>
    static string? Picked(IReadOnlyList<IReadOnlyList<MenuWord>> lines)
    {
        var button = lines.FirstOrDefault(l => Squash(Text(l)).Contains("전체현황"));
        if (button is null) return null;
        double top = button.Min(w => w.Y), cx = button.Average(w => w.CenterX);
        var header = lines.Where(l => l != button && l.Max(w => w.Y + w.H) <= top + 2 && top - l.Max(w => w.Y + w.H) < HeaderGap)
            .Select(l => l.Where(w => Math.Abs(w.CenterX - cx) < 150).ToList()).Where(l => l.Count > 0)
            .MaxBy(l => l.Max(w => w.Y));
        return header is null ? null : CharacterName.Clean(string.Concat(header.Select(w => w.Text)));
    }

    /// <summary>The card of the character being played: "접속 중" where its name would be (it has no name to read).</summary>
    static MenuCard? PlayingCardOf(IReadOnlyList<IReadOnlyList<MenuWord>> lines)
    {
        var words = lines.SelectMany(l => l).ToList();
        foreach (var lv in words.Where(w => LevelWord().Match(w.Text) is { Success: true } m && m.Length == w.Text.Length))
            if (words.Any(w => lv.CenterY - w.CenterY > NameMin && lv.CenterY - w.CenterY <= NameMax && w.X >= lv.X + NameFrom && w.X < lv.X + NameTo && w.Text.Contains("접속")))
                return new MenuCard("", null, lv.X - CardLeft, lv.Y, CardWidth, lv.Y + lv.H + CardBelow);
        return null;
    }

    /// <summary>"접속 중" and the card's level word are often unreadable (goal.png): then it is the one gap in the grid of the cards read.
    /// The last row may be short, so its gaps don't count while another row has one; two gaps say nothing.</summary>
    static MenuCard? TheGap(IReadOnlyList<MenuCard> cards)
    {
        var cols = cards.GroupBy(c => Math.Round(c.X / CellSnap)).Select(g => g.First().X).ToList();
        var rows = cards.GroupBy(c => Math.Round(c.Y / CellSnap)).Select(g => g.First()).OrderBy(c => c.Y).ToList();
        if (cols.Count < 2 || rows.Count < 2) return null;
        bool Seen(double x, double y) => cards.Any(c => Math.Abs(c.X - x) < CellSnap && Math.Abs(c.Y - y) < CellSnap);
        var gaps = rows.SelectMany(r => cols.Where(x => !Seen(x, r.Y)).Select(x => r with { Name = "", ClassName = null, ItemLevel = null, X = x })).ToList();
        var early = gaps.Where(g => g.Y != rows[^1].Y).ToList();
        return early.Count == 1 ? early[0] : early.Count == 0 && gaps.Count == 1 ? gaps[0] : null;
    }

    /// <summary>Each card: a level word ("Lv.70", read "LⅥ70") with its name and class above it (the OCR puts the level and the
    /// item level on lines of their own, so position, not line, ties them). Top to bottom, left to right.</summary>
    static IReadOnlyList<MenuCard> Cards(IReadOnlyList<IReadOnlyList<MenuWord>> lines)
    {
        var words = lines.SelectMany(l => l).ToList();
        var cards = new List<MenuCard>();
        foreach (var line in lines)
            foreach (var lv in line.Where(w => LevelWord().Match(w.Text) is { Success: true } m && m.Length == w.Text.Length))
            {
                string? Above(double from, double to) =>
                    string.Concat(words.Where(w => lv.CenterY - w.CenterY > from && lv.CenterY - w.CenterY <= to && w.X >= lv.X + NameFrom && w.X < lv.X + NameTo)
                        .OrderBy(w => w.X).Select(w => w.Text)) is { Length: > 0 } t ? t : null;
                if (Above(NameMin, NameMax) is not { } raw || raw.Contains("접속") || CharacterName.Clean(raw) is not { } name) continue;
                string? cls = Above(NameMax, ClassMax) is { } c ? CharacterName.Clean(c) : null;
                int? level = ItemLevel(string.Concat(words.Where(w => w != lv && Math.Abs(w.CenterY - lv.CenterY) <= SameLine && w.X > lv.X && w.X < lv.X + NameTo).OrderBy(w => w.X).Select(w => w.Text)));
                cards.Add(new MenuCard(name, cls, lv.X - CardLeft, lv.Y, CardWidth, lv.Y + lv.H + CardBelow, level));
            }
        return [.. cards.OrderBy(c => Math.Round(c.Y / 20)).ThenBy(c => c.X)];
    }

    static string Text(IEnumerable<MenuWord> line) => string.Join(' ', line.Select(w => w.Text));

    static string Squash(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>"Lv.70": the OCR reads "v" as "Ⅵ" / "Ⅴ" and may drop the dot.</summary>
    [GeneratedRegex(@"^L[vVⅤⅥ][.,]?\d{1,3}")]
    private static partial Regex LevelWord();

    [GeneratedRegex(@"\d{4}")]
    private static partial Regex FourDigits();

    [GeneratedRegex(@"(?<!\d)(\d{3})[.,]\d{2}")]
    private static partial Regex NoLeadingOne();

    // Any first and third letter: the OCR's guesses at the title's font keep changing (게 / 계, 메 / 매 / 에; 에 alone hid the
    // menu for a whole session). A wrong match costs one panel read, which finds no title and says closed.
    [GeneratedRegex("[가-힣]임[가-힣]뉴")]
    private static partial Regex Title();

    [GeneratedRegex("[잠장삼]시만기다")]   // "잠시만 기다려주세요." read "장시만", "삼시만", "기다러" (--read-menu, 2026-11-04)
    private static partial Regex Waiting();
}
