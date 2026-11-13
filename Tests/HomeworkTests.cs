using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>숙제 (homework.plan.md): the Wednesday 06:00 week, which fights clear a raid, the tags and raising a pick.</summary>
public class HomeworkTests
{
    static readonly IReadOnlyList<RaidGate> Gates =
    [
        new("serka-g1", "고통의 마녀", "1관문", [], [], Raid.없음),
        new("serka-g2", "고통의 마녀", "2관문", [], [], Raid.없음),
    ];

    static readonly HomeworkRaid Serka = new("serka", "세르카", ["serka-g1", "serka-g2"],
    [
        new("노말", "세노", HomeworkColor.White, ["serka-g2"]),
        new("하드", "세하", HomeworkColor.Orange, ["serka-g2"]),
        new("나이트메어", "세나메", HomeworkColor.Purple, ["serka-g2"]),
    ]);

    static readonly IReadOnlyList<HomeworkRaid> Raids = [Serka];

    static FightRecord Fight(string gate, string difficulty, bool? cleared = true, string? character = "살살", int day = 4, int hour = 20) =>
        new(new DateTime(2026, 11, day, hour, 0, 0), gate, 300, [], "나", 3, null, [], [], [], [], Difficulty: difficulty, Cleared: cleared, Character: character);

    static HomeworkClear Clear(string difficulty, int day = 4, int hour = 20) => new("살살", "serka", difficulty, new DateTime(2026, 11, day, hour, 0, 0));

    [Fact]
    public void A_clear_marked_by_hand_crosses_a_shipped_raid_out_until_the_next_reset()
    {
        string path = Path.Combine(Path.GetTempPath(), $"homework-{Guid.NewGuid():N}.json");
        try
        {
            var store = new HomeworkStore(path);
            var pick = new HomeworkPick("serka", "하드");
            var marked = Clear("하드", day: 10, hour: 22);   // Tuesday night, the last hours of the week
            Assert.True(store.Toggle(marked));

            HomeworkTag? TagAt(DateTime now) => Homework.Tag(pick, store.Load(), Raids, new Dictionary<string, string>(), Homework.WeekStart(now));
            Assert.True(TagAt(marked.At)!.Done);
            Assert.False(TagAt(new DateTime(2026, 11, 11, 6, 0, 0))!.Done);   // Wednesday 06:00

            Assert.False(store.Toggle(marked with { At = marked.At.AddMinutes(1) }));   // a second click takes it back
            Assert.False(TagAt(marked.At)!.Done);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(2026, 11, 4, 6, 0, 2026, 11, 4)]     // Wednesday 06:00 itself starts the week
    [InlineData(2026, 11, 4, 5, 59, 2026, 10, 28)]   // a minute before: last week
    [InlineData(2026, 11, 10, 23, 0, 2026, 11, 4)]   // Tuesday night: still the week from Wednesday
    [InlineData(2026, 11, 8, 12, 0, 2026, 11, 4)]
    public void A_week_starts_on_wednesday_at_six(int y, int mo, int d, int h, int mi, int wy, int wmo, int wd)
    {
        Assert.Equal(new DateTime(wy, wmo, wd, 6, 0, 0), Homework.WeekStart(new DateTime(y, mo, d, h, mi, 0)));
    }

    [Fact]
    public void A_cleared_last_gate_of_a_known_character_is_a_clear()
    {
        var clear = Homework.ClearOf(Fight("고통의 마녀 · 2관문", "하드"), Gates, Raids);

        Assert.Equal(new HomeworkClear("살살", "serka", "하드", new DateTime(2026, 11, 4, 20, 0, 0)), clear);
    }

    [Theory]
    [InlineData("고통의 마녀 · 1관문", "하드", true, "살살")]    // not the last gate
    [InlineData("고통의 마녀 · 2관문", "하드", false, "살살")]   // failed
    [InlineData("고통의 마녀 · 2관문", "하드", null, "살살")]    // result unknown
    [InlineData("고통의 마녀 · 2관문", "하드", true, null)]      // character unknown
    [InlineData("고통의 마녀 · 2관문", "매칭", true, "살살")]    // a difficulty the raid doesn't track
    [InlineData("구원의 종탑", "3단계", true, "살살")]           // no known gate
    public void Other_fights_clear_nothing(string gate, string difficulty, bool? cleared, string? character)
    {
        Assert.Null(Homework.ClearOf(Fight(gate, difficulty, cleared, character), Gates, Raids));
    }

    [Fact]
    public void An_open_pick_shows_its_own_tag()
    {
        var tag = Homework.Tag(new HomeworkPick("serka", "나이트메어"), [], Raids, new Dictionary<string, string>(), Week);

        Assert.Equal(new HomeworkTag("세나메", HomeworkColor.Purple, Done: false, Arrow: 0), tag);
    }

    static readonly DateTime Week = new(2026, 11, 4, 6, 0, 0);

    [Fact]
    public void A_clear_this_week_crosses_the_tag_out_with_the_difficulty_cleared()
    {
        var pick = new HomeworkPick("serka", "나이트메어");

        Assert.Equal(new HomeworkTag("세나메", HomeworkColor.Purple, true, 0), Homework.Tag(pick, [Clear("나이트메어")], Raids, new Dictionary<string, string>(), Week));
        Assert.Equal(new HomeworkTag("세하", HomeworkColor.Orange, true, -1), Homework.Tag(pick, [Clear("하드")], Raids, new Dictionary<string, string>(), Week));
        Assert.Equal(new HomeworkTag("세나메", HomeworkColor.Purple, true, 1),
            Homework.Tag(new HomeworkPick("serka", "하드"), [Clear("나이트메어")], Raids, new Dictionary<string, string>(), Week));
    }

    [Fact]
    public void A_clear_from_last_week_does_not_count()
    {
        var tag = Homework.Tag(new HomeworkPick("serka", "하드"), [Clear("하드", day: 4, hour: 5)], Raids, new Dictionary<string, string>(), Week);

        Assert.False(tag!.Done);
    }

    [Fact]
    public void A_renamed_tag_is_shown_instead_of_the_default()
    {
        var names = new Dictionary<string, string> { [Homework.TagKey("serka", "하드")] = "세르하" };

        Assert.Equal("세르하", Homework.Tag(new HomeworkPick("serka", "하드"), [], Raids, names, Week)!.Text);
    }

    [Fact]
    public void A_pick_of_a_raid_no_longer_known_has_no_tag()
    {
        Assert.Null(Homework.Tag(new HomeworkPick("gone", "하드"), [], Raids, new Dictionary<string, string>(), Week));
    }

    [Fact]
    public void A_higher_clear_raises_the_pick_and_a_lower_one_keeps_it()
    {
        IReadOnlyList<HomeworkPick> picks = [new("serka", "하드")];

        Assert.Equal([new HomeworkPick("serka", "나이트메어")], Homework.Raise(picks, Clear("나이트메어"), Raids));
        Assert.Same(picks, Homework.Raise(picks, Clear("노말"), Raids));
        Assert.Same(picks, Homework.Raise(picks, Clear("하드"), Raids));
    }

    // The entry levels the user gave (2026-11-04), in raids.json's order.
    static readonly IReadOnlyList<HomeworkRaid> Entries =
    [
        new("act3", "3막", ["act3-g3"], [new("노말", "3노", HomeworkColor.White, ["act3-g3"])]),   // no entry level: never allocated
        R("act4", ("싱글", 1700), ("노말", 1700), ("하드", 1720)),
        R("final", ("싱글", 1710), ("노말", 1710), ("하드", 1730)),
        R("serka", ("매칭", 1710), ("노말", 1710), ("하드", 1730), ("나이트메어", 1740)),
        R("belgardin", ("매칭", 1750), ("노말", 1750), ("하드", 1770), ("나이트메어", 1780)),
        R("belltower", ("1단계", 1700), ("2단계", 1720), ("3단계", 1750)),
    ];

    static HomeworkRaid R(string id, params (string Difficulty, int Entry)[] levels) =>
        new(id, id, [id], [.. levels.Select(l => new HomeworkLevel(l.Difficulty, l.Difficulty, HomeworkColor.White, [id], l.Entry))]);

    static HomeworkPick[] P(params (string Raid, string Difficulty)[] picks) => [.. picks.Select(p => new HomeworkPick(p.Raid, p.Difficulty))];

    [Fact]
    public void A_level_gets_the_three_raids_with_the_highest_entry_it_meets()
    {
        Assert.Equal(P(("belltower", "2단계"), ("act4", "하드"), ("serka", "노말")), Homework.Allocate(1720, Entries));   // 세르카's top (1740) beats 종막's (1730)
        Assert.Equal(P(("belgardin", "노말"), ("belltower", "3단계"), ("serka", "나이트메어")), Homework.Allocate(1750, Entries));   // a tie: 벨가르딘's top (1780) first
        Assert.Equal(P(("belgardin", "하드"), ("belltower", "3단계"), ("serka", "나이트메어")), Homework.Allocate(1775, Entries));
        Assert.Equal(P(("belgardin", "나이트메어"), ("belltower", "3단계"), ("serka", "나이트메어")), Homework.Allocate(1780, Entries));
        Assert.Equal(P(("serka", "노말"), ("final", "노말"), ("belltower", "1단계")), Homework.Allocate(1710, Entries));   // the higher of two at one entry level
        Assert.Equal(P(("belltower", "1단계"), ("act4", "노말")), Homework.Allocate(1700, Entries));   // only two open
    }

    [Fact]
    public void A_level_under_every_entry_gets_no_raids()
    {
        Assert.Empty(Homework.Allocate(1640, Entries));
    }

    [Fact]
    public void A_first_level_allocates_only_a_character_without_picks()
    {
        IReadOnlyList<HomeworkPick> mine = P(("act3", "노말"));

        Assert.Equal(Homework.Allocate(1750, Entries), Homework.PicksAfter([], null, 1750, follow: false, Entries));
        Assert.Same(mine, Homework.PicksAfter(mine, null, 1750, follow: true, Entries));
    }

    [Fact]
    public void A_new_level_redoes_the_picks_only_while_the_switch_is_on()
    {
        IReadOnlyList<HomeworkPick> mine = P(("act3", "노말"));

        Assert.Equal(Homework.Allocate(1775, Entries), Homework.PicksAfter(mine, 1750, 1775, follow: true, Entries));
        Assert.Same(mine, Homework.PicksAfter(mine, 1750, 1775, follow: false, Entries));
        Assert.Same(mine, Homework.PicksAfter(mine, 1750, 1750, follow: true, Entries));
        Assert.Same(mine, Homework.PicksAfter(mine, 1650, 1660, follow: true, Entries));   // no raid open: nothing to replace them with
    }

    [Theory]
    [InlineData(null, 1750, 1750)]
    [InlineData(1750, 1755, 1755)]
    [InlineData(1750, 1740, 1750)]   // a level doesn't fall: a misread
    [InlineData(1750, 1950, 1750)]   // nor jump: a misread (a real one is typed on 설정 → 숙제)
    public void A_read_level_moves_only_up_and_by_a_believable_step(int? stored, int read, int kept)
    {
        Assert.Equal(kept, Homework.LevelFrom(stored, read));
    }

    static HomeworkCustomRaid Ex(bool auto = false) => new("custom-ex", "종막 EX",
        [new("EX 하드", "EX하", HomeworkColor.Orange, []), new("EX 나이트메어", "EX나메", HomeworkColor.Purple, [])], auto);

    static IReadOnlyList<HomeworkRaid> WithEx => [.. Entries, Homework.AsRaid(Ex())];

    [Fact]
    public void An_added_raid_is_made_from_its_filled_rows()
    {
        var raid = Homework.NewCustom("id", " 종막 EX ", [("EX 하드", "EX하", HomeworkColor.Orange), ("", "", HomeworkColor.White), ("", "EX나메", HomeworkColor.Purple)], auto: true);

        Assert.Equal("종막 EX", raid!.Name);
        Assert.True(raid.Auto);
        Assert.Equal([("EX 하드", "EX하"), ("EX나메", "EX나메")], raid.Levels.Select(l => (l.Difficulty, l.Tag)));   // a tag alone names its difficulty too
    }

    [Theory]
    [InlineData("", "EX 하드")]    // no name
    [InlineData("종막 EX", "")]    // no difficulty
    public void An_added_raid_needs_a_name_and_a_difficulty(string name, string difficulty)
    {
        Assert.Null(Homework.NewCustom("id", name, [(difficulty, "", HomeworkColor.White)], auto: false));
    }

    [Fact]
    public void An_added_raid_is_never_allocated_and_stays_on_top_of_the_three()
    {
        IReadOnlyList<HomeworkPick> mine = P(("act4", "노말"), ("custom-ex", "EX 하드"));

        Assert.DoesNotContain(Homework.Allocate(1800, WithEx), p => p.Raid == "custom-ex");
        Assert.Equal([.. Homework.Allocate(1775, WithEx), new HomeworkPick("custom-ex", "EX 하드")], Homework.PicksAfter(mine, 1750, 1775, follow: true, WithEx));
    }

    [Fact]
    public void A_first_level_still_gives_the_three_to_a_character_with_only_added_raids()
    {
        IReadOnlyList<HomeworkPick> mine = P(("custom-ex", "EX 하드"));

        Assert.Equal([.. Homework.Allocate(1750, WithEx), mine[0]], Homework.PicksAfter(mine, null, 1750, follow: false, WithEx));
    }

    [Fact]
    public void Added_raids_past_five_picks_are_left_off()
    {
        IReadOnlyList<HomeworkPick> mine = P(("custom-ex", "EX 하드"), ("custom-ex", "EX 나이트메어"), ("custom-ex", "EX 하드"));

        Assert.Equal(HomeworkCharacter.MaxPicks, Homework.PicksAfter(mine, 1750, 1775, follow: true, WithEx).Count);
    }

    [Fact]
    public void An_automatic_raid_goes_on_at_its_top_difficulty_when_there_is_room()
    {
        var ex = Homework.AsRaid(Ex());
        IReadOnlyList<HomeworkPick> full = [.. Enumerable.Range(0, 5).Select(i => new HomeworkPick($"r{i}", "노말"))];
        IReadOnlyList<HomeworkPick> has = P(("custom-ex", "EX 하드"));

        Assert.Equal(P(("custom-ex", "EX 나이트메어")), Homework.WithRaid([], ex));
        Assert.Same(full, Homework.WithRaid(full, ex));
        Assert.Same(has, Homework.WithRaid(has, ex));
    }

    [Fact]
    public void A_clear_of_an_added_raid_crosses_its_tag_out_and_no_fight_can_make_one()
    {
        var clear = new HomeworkClear("살살", "custom-ex", "EX 하드", new DateTime(2026, 11, 4, 20, 0, 0));
        var tag = Homework.Tag(new HomeworkPick("custom-ex", "EX 나이트메어"), [clear], WithEx, new Dictionary<string, string>(), Week);

        Assert.Equal(new HomeworkTag("EX하", HomeworkColor.Orange, true, -1), tag);
        Assert.Null(Homework.ClearOf(Fight("종막 EX", "EX 하드"), Gates, WithEx));
    }

    [Fact]
    public void Added_raids_join_the_shipped_ones_unless_broken()
    {
        var broken = new HomeworkCustomRaid("custom-b", null!, [], false);
        var outsider = Ex() with { Id = "act4" };   // could shadow a shipped raid

        Assert.Equal([.. Entries.Select(r => r.Id), "custom-ex"], Homework.AllRaids(Entries, [broken, outsider, Ex()]).Select(r => r.Id));
    }

    [Fact]
    public void A_click_clears_an_added_raid_and_another_takes_it_back()
    {
        string path = Path.Combine(Path.GetTempPath(), $"homework-{Guid.NewGuid():N}.json");
        try
        {
            var store = new GaugeOverlay.Core.Data.HomeworkStore(path);
            var clear = new HomeworkClear("살살", "custom-ex", "EX 하드", new DateTime(2026, 11, 4, 20, 0, 0));
            var other = clear with { Character = "밝키리" };
            store.Toggle(other);

            Assert.True(store.Toggle(clear));
            Assert.Contains(clear, store.Load());
            Assert.False(store.Toggle(clear with { At = clear.At.AddHours(1) }));
            Assert.Equal([other], store.Load());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Grid_cards_go_to_the_nearest_listed_character_and_the_played_card_to_who_is_played()
    {
        HomeworkCharacter C(string name, bool hidden = false, int picks = 1) =>
            new() { Name = name, Hidden = hidden, Picks = [.. Enumerable.Repeat(new HomeworkPick("serka", "노말"), picks)] };
        MenuCard Card(string name, double x) => new(name, null, x, 426, 245, 452);
        var (mine, other, gone, none) = (C("너무아파요살살"), C("매우아파요살살"), C("밝키리", hidden: true), C("도화G", picks: 0));
        var read = new MenuRead(true, "너무아파요살살", null, [Card("매우Ot파요살살", 846), Card("밞기리", 1106), Card("도하G", 586), Card("곰과역우와", 846)], false,
            PlayingCard: Card("", 586));

        var grid = Homework.OnGrid(read, "너무아파요살살", [mine, other, gone, none]);

        Assert.Equal([("매우Ot파요살살", other), ("", mine)], grid.Select(x => (x.Card.Name, x.Character)));
        Assert.DoesNotContain(Homework.OnGrid(read, null, [mine, other]), x => x.Card.Name == "");   // 캐릭터 불명: the played card is nobody's yet
    }

    static HomeworkCharacter Lv(string name, int? level) => new() { Name = name, ItemLevel = level, Picks = [new HomeworkPick("serka", "노말")] };

    [Fact]
    public void Two_cards_read_alike_go_to_the_characters_their_levels_fit()
    {
        // The user's grid, 2026-11-08: the small font read both 너무아파요살살 (1791) and 너무아파용살살 (1755) as "너무Ot파용살살".
        IReadOnlyList<HomeworkCharacter> listed = [Lv("너무아파요살살", 1791), Lv("너무아파용살살", 1755)];

        Assert.Equal("너무아파요살살", Homework.CardOwner(new MenuCard("너무Ot파용살살", null, 0, 0, 245, 0, 1791), listed));
        Assert.Equal("너무아파용살살", Homework.CardOwner(new MenuCard("너무Ot파용살살", null, 0, 0, 245, 0, 1755), listed));
    }

    [Fact]
    public void A_card_whose_level_rose_still_goes_to_its_character_not_to_one_it_would_lower()
        => Assert.Equal("너무아파용살살", Homework.CardOwner(new MenuCard("너무Ot파용살살", null, 0, 0, 245, 0, 1760),
            [Lv("너무아파요살살", 1791), Lv("너무아파용살살", 1755)]));

    [Fact]
    public void A_rise_both_look_alikes_could_have_leaves_it_to_the_name_not_the_smaller_rise()
        // 용 really rose 1700 -> 1730; 요 (1720) would rise less. The smaller rise must not take the card and lift 요 to 1730.
        => Assert.Equal("너무아파용살살", Homework.CardOwner(new MenuCard("너무Ot파용살살", null, 0, 0, 245, 0, 1730),
            [Lv("너무아파요살살", 1720), Lv("너무아파용살살", 1700)]));

    static HomeworkCharacter LvClass(string name, int? level, string? cls) => new() { Name = name, ItemLevel = level, ClassName = cls };

    [Fact]
    public void A_card_misread_past_any_name_is_the_one_character_at_its_level_and_class()
    {
        // The user's grid, 2026-11-08: 검지관절브레이커 (1752) read "김시관절브래0P1", too far for its name, and listed twice.
        IReadOnlyList<HomeworkCharacter> listed = [LvClass("검지관절브레이커", 1752, null), LvClass("도화G", 1640, "도화가"), LvClass("공과여우와티라노", 1640, "환수사")];
        var card = new MenuCard("김시관절브래0P1", "브레이커", 0, 0, 245, 0, 1752);

        Assert.Equal("검지관절브레이커", Homework.CardOwner(card, listed));
        Assert.Null(Homework.SureOwner(card, listed));   // level alone: its level goes there, a class (kept for good) doesn't
    }

    [Fact]
    public void A_far_name_at_a_shared_level_or_another_class_is_a_new_character()
    {
        IReadOnlyList<HomeworkCharacter> listed = [LvClass("도화G", 1640, "도화가"), LvClass("공과여우와티라노", 1640, "환수사"), LvClass("배럭3호봉", 1701, "블래스터")];
        Assert.Null(Homework.CardOwner(new MenuCard("바드바드", "바드", 0, 0, 245, 0, 1640), listed));   // two at 1640
        Assert.Null(Homework.CardOwner(new MenuCard("바드바드", "바드", 0, 0, 245, 0, 1701), listed));   // 1701 is a 블래스터
    }

    [Fact]
    public void Only_a_sure_owner_takes_a_cards_class_its_exact_name_or_its_exact_level()
    {
        IReadOnlyList<HomeworkCharacter> listed = [Lv("너무아파요살살", 1791), Lv("밝키", null)];
        Assert.Equal("너무아파요살살", Homework.SureOwner(new MenuCard("너무아파요살살", null, 0, 0, 245, 0, 1700), listed));   // read exactly
        Assert.Equal("너무아파요살살", Homework.SureOwner(new MenuCard("너무Ot파요살살", null, 0, 0, 245, 0, 1791), listed));   // its level
        Assert.Null(Homework.SureOwner(new MenuCard("너무Ot파요살살", null, 0, 0, 245, 0, 1800), listed));   // a rise: owner, not sure
        Assert.Null(Homework.SureOwner(new MenuCard("밝기", null, 0, 0, 245, 0, 1700), listed));   // a near name alone: maybe a new alt
    }

    [Fact]
    public void A_card_read_exactly_is_that_character_whatever_its_level_reads()
        => Assert.Equal("배럭3호봉", Homework.CardOwner(new MenuCard("배럭3호봉", null, 0, 0, 245, 0, 1761),
            [Lv("배럭3호봉", 1701), Lv("배럭4호봉", 1761)]));

    [Fact]
    public void Without_a_level_a_card_goes_to_the_nearest_name_and_a_tie_to_none()
    {
        IReadOnlyList<HomeworkCharacter> listed = [Lv("너무아파요살살", 1791), Lv("너무아파용살살", 1755)];
        Assert.Equal("너무아파용살살", Homework.CardOwner(new MenuCard("너무Ot파용살살", null, 0, 0, 245, 0), listed));
        Assert.Null(Homework.CardOwner(new MenuCard("너무아파오살살", null, 0, 0, 245, 0), listed));
        Assert.Null(Homework.CardOwner(new MenuCard("김뚜까뚜까", null, 0, 0, 245, 0, 1791), listed));
    }

    [Fact]
    public void A_level_that_fits_no_one_leaves_it_to_the_name()
    {
        IReadOnlyList<HomeworkCharacter> listed = [Lv("너무아파요살살", 1791), Lv("너무아파용살살", 1755)];
        Assert.Equal(Homework.CardOwner(new MenuCard("너무Ot파용살살", null, 0, 0, 245, 0), listed),
            Homework.CardOwner(new MenuCard("너무Ot파용살살", null, 0, 0, 245, 0, 1751), listed));   // a dropped digit: both would fall, as with no level
        Assert.Equal("너무아파용살살", Homework.CardOwner(new MenuCard("너무Ot파용살살", null, 0, 0, 245, 0, 1700),
            [Lv("너무아파요살살", null), Lv("너무아파용살살", 1755)]));   // unknown and falling alike: the nearer name
    }

    [Fact]
    public void A_card_too_far_from_the_nearest_name_is_no_one_even_if_a_longer_name_allows_it()
        // 3 letters from both; the short name allows 2, the long one 4: Nearest's rule (the nearest name's own tolerance) gives none.
        => Assert.Null(Homework.CardOwner(new MenuCard("가나다라마", null, 0, 0, 245, 0), [Lv("가나바사아", 1700), Lv("가나다라마바사아", 1700)]));

    [Theory]
    [InlineData("1729.99", 1729)]
    [InlineData(" 1730 ", 1730)]
    [InlineData("1729,99", null)]
    [InlineData("abc", null)]
    [InlineData("2500", null)]
    public void A_typed_level_drops_its_decimals_and_never_rounds_up(string typed, int? expected)
        => Assert.Equal(expected, Homework.TypedLevel(typed));   // 1729.99 can't enter a 1730 raid (user, 2026-11-08)

    [Fact]
    public void The_grid_tags_follow_the_levels()
    {
        var (yo, yong) = (Lv("너무아파요살살", 1791), Lv("너무아파용살살", 1755));
        var read = new MenuRead(true, null, null,
            [new("너무Ot파용살살", null, 846, 426, 245, 452, 1791), new("너무Ot파용살살", null, 1106, 426, 245, 452, 1755)], false);

        var grid = Homework.OnGrid(read, null, [yo, yong]);

        Assert.Equal([yo, yong], grid.Select(x => x.Character));
    }

    static string TempFile() => Path.Combine(Path.GetTempPath(), $"homework-{Guid.NewGuid():N}.json");

    [Fact]
    public void A_clears_file_from_before_the_unsure_state_loads_as_clears()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, "[{\"Character\":\"살살\",\"Raid\":\"serka\",\"Difficulty\":\"하드\",\"At\":\"2026-11-04T20:00:00\"}]");

            var loaded = new HomeworkStore(path).Load();

            Assert.False(Assert.Single(loaded).Unsure);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_maybe_never_replaces_a_sure_clear_and_a_sure_one_replaces_a_maybe()
    {
        string path = TempFile();
        try
        {
            var store = new HomeworkStore(path);
            var now = new DateTime(2026, 11, 5, 12, 0, 0);
            var maybe = Clear("하드", day: 5, hour: 11) with { Unsure = true };
            var sure = Clear("하드", day: 5, hour: 10);

            Assert.True(store.Add(maybe, now));
            Assert.True(store.Add(sure, now));
            Assert.False(store.Add(maybe with { At = maybe.At.AddMinutes(1) }, now));

            Assert.Equal([sure], store.Load());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_three_parts_set_this_weeks_state_of_a_raid()
    {
        string path = TempFile();
        try
        {
            var store = new HomeworkStore(path);
            var now = new DateTime(2026, 11, 5, 12, 0, 0);
            var pick = new HomeworkPick("serka", "하드");
            HomeworkTag? Tag() => Homework.Tag(pick, store.Load(), Raids, new Dictionary<string, string>(), Homework.WeekStart(now));

            store.Set("살살", "serka", now, Clear("하드", day: 5, hour: 12) with { Unsure = true });
            Assert.True(Tag()!.Unsure && Tag()!.Done);

            store.Set("살살", "serka", now, Clear("하드", day: 5, hour: 12));
            Assert.True(Tag()!.Done && !Tag()!.Unsure);

            store.Set("살살", "serka", now, null);
            Assert.False(Tag()!.Done);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_retry_takes_back_the_fallbacks_clear_but_not_one_set_by_hand_since()
    {
        string path = TempFile();
        try
        {
            var store = new HomeworkStore(path);
            var now = new DateTime(2026, 11, 5, 12, 0, 0);
            var fallback = Clear("하드", day: 5, hour: 11) with { Unsure = true };
            store.Add(fallback, now);
            var hand = Clear("하드", day: 5, hour: 12);
            store.Set(hand.Character, hand.Raid, now, hand);   // 클리어 clicked after the fallback's 확인필요

            Assert.False(store.Remove(fallback));   // the retry
            Assert.Equal([hand], store.Load());

            store.Set(hand.Character, hand.Raid, now, null);
            store.Add(fallback, now);
            Assert.True(store.Remove(fallback));
            Assert.Empty(store.Load());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, false, HomeworkPart.미완)]
    [InlineData(true, false, HomeworkPart.클리어)]
    [InlineData(true, true, HomeworkPart.확인필요)]
    public void The_lit_part_follows_the_tag(bool done, bool unsure, HomeworkPart expected) =>
        Assert.Equal(expected, Homework.PartOf(new HomeworkTag("세하", HomeworkColor.Orange, done, 0, unsure)));

    [Theory]
    [InlineData(true, true, true, HomeworkSource.RecordedFight)]
    [InlineData(true, true, false, HomeworkSource.RecordedFight)]   // the recording decides, never the fallback beside it
    [InlineData(true, false, false, HomeworkSource.BossBar)]        // the meter's own bar read
    [InlineData(false, true, true, HomeworkSource.BossBar)]
    [InlineData(false, true, false, HomeworkSource.None)]           // no bar read: no automatic clear
    public void The_source_of_automatic_clears_is_never_two(bool meterOn, bool record, bool bossHpRead, HomeworkSource expected) =>
        Assert.Equal(expected, Homework.SourceOf(meterOn, record, bossHpRead));

    [Theory]
    [InlineData(true, HomeworkSource.RecordedFight)]
    [InlineData(false, HomeworkSource.BossBar)]   // no panel, no record to come: the meter's own bar read decides
    public void With_the_meter_and_records_on_the_panel_says_who_decides(bool panelRead, HomeworkSource expected) =>
        Assert.Equal(expected, Homework.SourceOf(meterOn: true, record: true, bossHpRead: false, panelRead));

    [Fact]
    public void At_most_five_picks_per_character()
    {
        Assert.Equal(5, HomeworkCharacter.MaxPicks);
    }
}
