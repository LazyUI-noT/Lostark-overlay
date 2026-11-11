using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Reading the game menu's OCR (homework.plan.md): positions at 1080p from reference/homework_project/goal.png.</summary>
public class GameMenuTests
{
    static MenuWord W(string text, double x, double y, double w = 120, double h = 14) => new(text, x, y, w, h);
    static IReadOnlyList<MenuWord> L(params MenuWord[] words) => words;

    // The grid open on 너무아파요살살, 매우아파요살살 picked (its name in the right panel's header).
    static readonly IReadOnlyList<IReadOnlyList<MenuWord>> Grid =
    [
        L(W("게임", 1040, 262, 40, 18), W("메뉴", 1085, 262, 40, 18)),
        L(W("매우아파요살살", 1528, 305)),
        L(W("전체", 1545, 332, 30), W("현황", 1580, 332, 30), W("보기", 1612, 332, 30)),
        L(W("아비도스", 920, 352, 70), W("주둔지", 995, 352, 50), W("엘네아드", 1200, 352, 70)),
        L(W("워로드", 900, 386, 50), W("디스트로이어", 1160, 386, 80)),
        L(W("접속", 683, 400, 30, 18), W("중", 716, 400, 20, 18), W("매우아파요살살", 900, 404), W("김뚜까뚜까", 1160, 404, 90)),
        L(W("Lv.70", 593, 424, 35), W("1791.66", 660, 424, 50), W("Lv.70", 853, 424, 35), W("1771.66", 920, 424, 50), W("Lv.70", 1113, 424, 35), W("1770.83", 1180, 424, 50)),
        L(W("브레이커", 640, 503, 50), W("가디언나이트", 900, 503, 80)),
        L(W("검지관절브레이커", 640, 521), W("너무아파용살살", 900, 521)),
        L(W("Lv.70", 593, 541, 35), W("1752.50", 660, 541, 50), W("Lv.70", 853, 541, 35), W("1755.00", 920, 541, 50)),
        L(W("Lv.70", 320, 676, 40, 16), W("너무아파요살살", 365, 676, 120, 16)),
    ];

    [Fact]
    public void The_grid_gives_the_current_and_picked_names_and_each_card()
    {
        var read = GameMenu.Parse(Grid, []);

        Assert.True(read.Open);
        Assert.Equal("너무아파요살살", read.Current);
        Assert.Equal("매우아파요살살", read.Picked);
        Assert.Equal(["매우아파요살살", "김뚜까뚜까", "검지관절브레이커", "너무아파용살살"], read.Cards.Select(c => c.Name));
        Assert.Equal(["워로드", "디스트로이어", "브레이커", "가디언나이트"], read.Cards.Select(c => c.ClassName));
        Assert.False(read.Waiting);
    }

    [Fact]
    public void A_card_sits_under_its_level_line()
    {
        var card = GameMenu.Parse(Grid, []).Cards[0];

        Assert.Equal(845, card.X, 1);
        Assert.True(card.Bottom > 424 + 14 && card.Bottom < 470);
    }

    [Fact]
    public void The_card_being_played_is_not_read_as_a_name()
    {
        Assert.DoesNotContain(GameMenu.Parse(Grid, []).Cards, c => c.Name.Contains("접속"));
    }

    [Fact]
    public void The_card_being_played_is_found_for_its_tags()
    {
        var card = GameMenu.Parse(Grid, []).PlayingCard;

        Assert.NotNull(card);
        Assert.Equal(585, card.X, 1);
        Assert.True(card.Bottom > 424 + 14 && card.Bottom < 470);
    }

    [Fact]
    public void A_misread_played_card_is_the_one_gap_in_the_grid()
    {
        // goal.png as the OCR reads it (--read-menu, 2026-11-04): "접속 중" comes out as 入A소, and the card has no level word.
        MenuWord[] Card(double x, double y, string name) => [W(name, x + 55, y - 21, 100), W("LⅥ70", x + 8, y, 35)];
        var cards = new[] { (846, 426), (1106, 426), (586, 543), (846, 543), (1106, 543), (586, 660), (846, 660) }.Select((c, i) => (IReadOnlyList<MenuWord>)Card(c.Item1, c.Item2, $"캐릭터이름{i}"));
        IReadOnlyList<IReadOnlyList<MenuWord>> lines = [L(W("계임", 1043, 262), W("매뉴", 1085, 262)), L(W("入A소", 683, 401)), .. cards];

        var playing = GameMenu.Parse(lines, []).PlayingCard;

        Assert.NotNull(playing);
        Assert.Equal((586d, 426d), (playing.X, playing.Y));
    }

    [Fact]
    public void Without_a_grid_no_card_is_the_played_one()
    {
        Assert.Null(GameMenu.Parse([L(W("계임", 1043, 262), W("매뉴", 1085, 262))], []).PlayingCard);
    }

    [Fact]
    public void The_waiting_text_after_a_pick_is_seen()
    {
        Assert.True(GameMenu.Parse(Grid, ["잠시만 기다려주세요."]).Waiting);
        Assert.True(GameMenu.Parse(Grid, ["잠시 만 기다려 주세요"]).Waiting);
    }

    [Fact]
    public void Without_the_title_the_menu_is_closed_and_nothing_is_read()
    {
        var read = GameMenu.Parse([.. Grid.Where(l => l[0].Text is not ("게임" or "전체"))], []);

        Assert.False(read.Open);
        Assert.Null(read.Current);
        Assert.Empty(read.Cards);
    }

    [Theory]
    [InlineData("게임 메뉴", true)]
    [InlineData("게임메뉴", true)]
    [InlineData("게임 설정", false)]
    public void The_title_strip_says_whether_the_menu_is_open(string line, bool open)
    {
        Assert.Equal(open, GameMenu.IsTitle([line]));
    }

    [Theory]
    [InlineData("너무아파요살살", "너무아파요살살")]
    [InlineData(" 배럭3호봉 ", "배럭3호봉")]
    [InlineData("도화G", "도화G")]
    [InlineData("너무아파요살살.", "너무아파요살살")]
    [InlineData("1791.66", null)]   // an item level
    [InlineData("가", null)]        // too short
    [InlineData("가Ot나", "가Ot나")]   // a typed name stays as typed
    [InlineData("수164이00912", null)]   // a number read with its icon (user, 2026-11-10: fights named after it)
    [InlineData("수177이83", null)]
    [InlineData("Ab12", "Ab12")]        // as many letters as digits is still a name
    public void A_name_is_letters_and_digits_with_a_letter(string text, string? name)
    {
        Assert.Equal(name, CharacterName.Clean(text));
    }

    [Theory]
    [InlineData("너무Ot파요살살", "너무아파요살살")]   // the small font's 아 reads "Ot" (user, 2026-11-08)
    [InlineData("Ot파요", "Ot파요")]                // only between Hangul letters
    [InlineData("Otto", "Otto")]
    [InlineData("1791.66", null)]
    public void A_card_name_puts_back_the_small_fonts_misreads(string text, string? name)
        => Assert.Equal(name, CharacterName.CardName(text));

    [Fact]
    public void A_misread_letter_snaps_to_the_known_name()
    {
        string[] known = ["너무아파요살살", "매우아파요살살"];

        Assert.Equal("너무아파요살살", CharacterName.Snap("너무아파8살살", known));
        Assert.Equal("매우아파요살살", CharacterName.Snap("매우아파요살살", known));
        Assert.Equal("새캐릭터", CharacterName.Snap("새캐릭터", known));
    }

    [Theory]
    [InlineData("Lazy0ne", "LazyOne")]
    [InlineData("Lazy1ne", "Lazylne")]
    public void A_letter_read_as_a_digit_is_a_misread(string read, string known)
    {
        Assert.Equal(known, CharacterName.Snap(read, [known]));
    }

    [Theory]
    [InlineData("너무아파용살살", "너무아파요살살")]   // the user's two characters, one not listed yet
    [InlineData("도화H", "도화G")]
    public void A_name_one_real_letter_off_a_known_one_is_another_character(string read, string known)
    {
        Assert.Equal(read, CharacterName.Snap(read, [known]));
    }

    // The video's card names as Windows OCR reads them (--read-menu, 2026-11-04), against the real names.
    static readonly string[] Roster = ["너무아파요살살", "매우아파요살살", "너무아파용살살", "김뚜까뚜까", "검지관절브레이커", "밝키리", "도화G", "배럭3호봉"];

    [Theory]
    [InlineData("매우Ot파요살살", "매우아파요살살")]
    [InlineData("너무Ot파용살살", "너무아파용살살")]   // 2 from it, 3 from 너무아파요살살
    [InlineData("김두까뚜t", "김뚜까뚜까")]
    [InlineData("김시관절브래이기", "검지관절브레이커")]
    [InlineData("밞기리", "밝키리")]
    [InlineData("도하G", "도화G")]
    [InlineData("배릐3호봉", "배럭3호봉")]
    public void A_misread_card_name_finds_the_nearest_known_name(string read, string name)
    {
        Assert.Equal(name, CharacterName.Nearest(read, Roster));
    }

    [Theory]
    [InlineData("곰과역우와EI라노")]   // nobody on the list
    [InlineData("아파요살")]            // as near to two names
    public void A_card_name_near_no_one_alone_matches_nobody(string read)
    {
        Assert.Null(CharacterName.Nearest(read, Roster));
    }

    [Fact]
    public void A_name_counts_once_read_the_same_twice_in_a_row()
    {
        var vote = NameVote.None;
        (vote, var first) = vote.Read("너무아파요살살");
        Assert.Null(first);
        (vote, var second) = vote.Read("너무아파요살살");
        Assert.Equal("너무아파요살살", second);

        (vote, var misread) = vote.Read("너무하파요살살");
        Assert.Null(misread);
        (_, var unread) = vote.Read(null);
        Assert.Null(unread);
    }

    [Theory]
    [InlineData("계임 매뉴", true)]
    [InlineData("계임 메뉴", true)]
    [InlineData("계임 에뉴", true)]   // the strip of a live 1080p menu, 2026-11-04: the panel's read of the same frame said 매뉴
    [InlineData("게임 설정", false)]
    [InlineData("메뉴", false)]
    public void The_title_is_found_as_the_ocr_reads_it(string line, bool open)
    {
        Assert.Equal(open, GameMenu.IsTitle([line]));
    }

    [Fact]
    public void Level_words_read_as_roman_numerals_still_place_the_cards_and_the_portrait()
    {
        IReadOnlyList<IReadOnlyList<MenuWord>> lines =
        [
            L(W("계임", 1043, 262, 40, 18), W("매뉴", 1085, 262, 40, 18)),
            L(W("워르드", 900, 388, 50)),
            L(W("매우Ot파요살살", 901, 405)),
            L(W("LⅥ70", 854, 426, 35)),   // on a line of its own, its item level on another
            L(W("1771.66", 921, 427, 50)),
            L(W("LⅥ70", 320, 676, 40, 16), W("너무아파요살살", 372, 676, 120, 16)),
            L(W("장시만", 1660, 1005, 40), W("기다려주세요•", 1700, 1005, 80)),
        ];

        var read = GameMenu.Parse(lines, ["장시만 기다려주세요•"]);

        Assert.Equal("너무아파요살살", read.Current);
        Assert.Equal(["매우아파요살살"], read.Cards.Select(c => c.Name));
        Assert.True(read.Waiting);
    }

    [Theory]
    [InlineData("1771.66", 1771)]
    [InlineData("1640.OO", 1640)]     // as the OCR reads them (--read-menu, 2026-11-04)
    [InlineData("1755.예", 1755)]
    [InlineData("會|79|.66", 1791)]   // the icon, and 1 read as |
    [InlineData("습|791.66", 1791)]
    [InlineData("수1640.00", 1640)]
    [InlineData("쉬791.66", 1791)]    // the icon swallowed the 1
    [InlineData("00", null)]
    [InlineData("-", null)]
    [InlineData("9999.00", null)]     // no such level
    [InlineData("1639.99", null)]     // under 1640 and over 1900: a misread (user, 2026-11-08)
    [InlineData("1900.00", 1900)]
    [InlineData("1901.00", null)]
    [InlineData("쉬639.00", null)]    // the swallowed-1 read is bounded too
    public void An_item_level_is_found_in_what_the_ocr_reads(string text, int? level)
    {
        Assert.Equal(level, GameMenu.ItemLevel(text));
    }

    [Fact]
    public void The_menu_is_open_on_a_misread_title_or_on_the_right_panels_button()
    {
        Assert.True(GameMenu.Parse([L(W("계임", 1043, 262), W("몌뉴", 1085, 262))], []).Open);
        Assert.True(GameMenu.Parse([L(W("전체", 1545, 333), W("현황", 1576, 333), W("보기", 1607, 333))], []).Open);
        Assert.False(GameMenu.Parse([L(W("골드", 451, 305), W("획득", 478, 305))], []).Open);
    }

    [Fact]
    public void Cards_and_the_portrait_give_their_item_levels()
    {
        IReadOnlyList<IReadOnlyList<MenuWord>> lines =
        [
            L(W("계임", 1043, 262, 40, 18), W("매뉴", 1085, 262, 40, 18)),
            L(W("워르드", 900, 388, 50)),
            L(W("매우Ot파요살살", 901, 405)),
            L(W("LⅥ70", 854, 426, 35)),
            L(W("1771.66", 921, 427, 50)),
            L(W("LⅥ70", 320, 676, 40, 16), W("너무아파요살살", 372, 676, 120, 16)),
            L(W("會|79|.66", 357, 702, 60, 16)),
        ];

        var read = GameMenu.Parse(lines, []);

        Assert.Equal(1771, read.Cards[0].ItemLevel);
        Assert.Equal(1791, read.CurrentLevel);
    }

    [Fact]
    public void The_portrait_reads_when_the_ocr_splits_Lv_from_its_number()
    {
        // m4k.png (4K, --read-menu, 2026-11-09): "Lv." and "70" come as two words, where 1080p reads "LⅥ70".
        IReadOnlyList<IReadOnlyList<MenuWord>> lines =
        [
            L(W("계임", 1043, 262, 40, 18), W("메뉴", 1085, 262, 40, 18)),
            L(W("Lv.", 321, 677, 22, 16), W("70", 347, 677, 20, 16), W("너무아파요살살", 373, 676, 120, 16)),
            L(W("수", 357, 703, 12, 16), W("1791.66", 376, 704, 57, 16)),
        ];

        var read = GameMenu.Parse(lines, []);

        Assert.Equal("너무아파요살살", read.Current);
        Assert.Equal(1791, read.CurrentLevel);
    }

    [Fact]
    public void Cards_read_the_same_when_every_Lv_comes_split_from_its_number()
    {
        IReadOnlyList<IReadOnlyList<MenuWord>> split = [.. Grid.Select(l => (IReadOnlyList<MenuWord>)[.. l.SelectMany(w => w.Text.StartsWith("Lv.")
            ? new[] { w with { Text = "Lv.", W = 18 }, w with { Text = w.Text[3..], X = w.X + 20, W = w.W - 20 } }
            : [w])])];

        var (whole, apart) = (GameMenu.Parse(Grid, []), GameMenu.Parse(split, []));

        Assert.Equal(whole.Current, apart.Current);
        Assert.Equal(whole.Cards, apart.Cards);
    }

    [Fact]
    public void A_bare_Lv_before_a_word_that_is_no_number_is_left_alone()
    {
        IReadOnlyList<IReadOnlyList<MenuWord>> lines =
        [
            L(W("계임", 1043, 262, 40, 18), W("메뉴", 1085, 262, 40, 18)),
            L(W("Lv.", 321, 677, 22, 16), W("너무아파요살살", 373, 676, 120, 16)),
        ];

        Assert.Null(GameMenu.Parse(lines, []).Current);
    }

    [Fact]
    public void A_read_near_two_known_names_is_kept_as_read()
    {
        Assert.Equal("아파요살살", CharacterName.Snap("아파요살살", ["너파요살살", "매파요살살"]));
    }

    [Fact]
    public void A_read_level_drops_its_decimals_and_never_rounds_up()
        => Assert.Equal(1729, GameMenu.ItemLevel("1729.99"));   // 1729.99 can't enter a 1730 raid (user, 2026-11-08)
}
