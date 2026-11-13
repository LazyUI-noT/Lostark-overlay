using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 다른 캐릭터 접속 grid's card order (card-order.plan.md): each character's card keeps its place, the grid scrolls by rows.</summary>
public class CardOrderTests
{
    // The user's roster (2026-11-08), grid order.
    static readonly string[] Roster = ["너무아파요살살", "매우아파요살살", "김뚜까뚜까", "검지관절브레이커", "너무아파용살살", "밞키리", "도화G", "공과여우와티라노", "배럭3호봉", "측법이축지법쓴다"];

    static Dictionary<int, string> Sure(params (int Slot, string Name)[] cards) => cards.ToDictionary(c => c.Slot, c => c.Name);

    [Fact]
    public void With_no_order_the_window_is_at_the_top()
        => Assert.Equal(0, CardOrder.Offset([], Sure((1, "매우아파요살살"))));

    [Fact]
    public void Without_a_sure_card_the_window_is_unknown()
        => Assert.Null(CardOrder.Offset(Roster, Sure()));

    [Fact]
    public void Sure_cards_find_the_window_by_whole_rows()
    {
        Assert.Equal(0, CardOrder.Offset(Roster, Sure((1, "매우아파요살살"), (5, "밞키리"))));
        Assert.Equal(3, CardOrder.Offset(Roster, Sure((0, "검지관절브레이커"), (6, "측법이축지법쓴다"))));   // scrolled a row
    }

    [Fact]
    public void A_sure_card_against_the_order_rules_its_window_out()
        // 매우 at slot 1 fits offset 0 only; 측법 at slot 2 would need offset 7, not a row: no window fits both.
        => Assert.Null(CardOrder.Offset(Roster, Sure((1, "매우아파요살살"), (2, "측법이축지법쓴다"))));

    [Fact]
    public void A_window_no_sure_card_agrees_with_is_unknown()
        => Assert.Null(CardOrder.Offset(["a", "b", "c", "a2", "b2", "c2"], Sure((0, "x"))));

    [Fact]
    public void An_unclear_card_takes_the_name_at_its_place()
    {
        Assert.Equal("너무아파요살살", CardOrder.Owner(Roster, 0, 0));
        Assert.Equal("너무아파용살살", CardOrder.Owner(Roster, 0, 4));
        Assert.Equal("측법이축지법쓴다", CardOrder.Owner(Roster, 3, 6));
        Assert.Null(CardOrder.Owner(Roster, 3, 7));   // past the end
    }

    [Fact]
    public void Learning_puts_sure_cards_in_their_places_and_moves_a_name_found_elsewhere()
    {
        var order = CardOrder.Learn([], 0, Sure((0, "a"), (2, "c")));
        Assert.Equal(["a", "", "c"], order);
        Assert.Null(CardOrder.Owner(order, 0, 1));   // a place not learned yet

        order = CardOrder.Learn(order, 0, Sure((1, "b"), (2, "a")));
        Assert.Equal(["", "b", "a"], order);   // one place per name
    }

    [Fact]
    public void A_row_past_the_end_is_a_window_for_new_cards()
    {
        var order = CardOrder.Learn(Roster, 9, Sure((0, "측법이축지법쓴다"), (1, "새캐릭터")));
        Assert.Equal("새캐릭터", order[10]);
        Assert.Equal(11, order.Count);
        Assert.Equal(9, CardOrder.Offset(order, Sure((0, "측법이축지법쓴다"))));
    }

    [Fact]
    public void A_read_no_window_fits_is_a_mismatch_once_two_sure_cards_say_so()
    {
        Assert.True(CardOrder.Mismatch(Roster, Sure((0, "매우아파요살살"), (1, "너무아파요살살"))));   // swapped
        Assert.False(CardOrder.Mismatch(Roster, Sure((0, "매우아파요살살"))));   // one card: not enough
        Assert.False(CardOrder.Mismatch(Roster, Sure((0, "너무아파요살살"), (1, "매우아파요살살"))));
        Assert.False(CardOrder.Mismatch([], Sure((0, "a"), (1, "b"))));
    }

    [Fact]
    public void A_rebuilt_order_starts_with_this_read_and_keeps_the_rest_after_it()
        => Assert.Equal(["b", "a", "c"], CardOrder.Rebuild(["a", "b", "c"], Sure((0, "b"), (1, "a"))));
}

/// <summary>The learned order and 「게임 순서 따르기」 are saved with the 숙제 list (card-order.plan.md).</summary>
public sealed class CardOrderSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "settings.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void No_order_is_learned_at_first_and_the_tab_follows_the_game()
    {
        var hw = new Settings().Homework;
        Assert.Empty(hw.Order);
        Assert.True(hw.FollowGameOrder);
    }

    [Fact]
    public void The_order_and_the_switch_survive_a_save()
    {
        Directory.CreateDirectory(_dir);
        var s = new Settings();
        (s.Homework.Order, s.Homework.FollowGameOrder) = (["a", "", "c"], false);
        new GaugeOverlay.Core.Data.SettingsStore(FilePath).Save(s);
        var back = new GaugeOverlay.Core.Data.SettingsStore(FilePath).Load(out _).Homework;
        Assert.Equal(["a", "", "c"], back.Order);
        Assert.False(back.FollowGameOrder);
    }
}

/// <summary>Whose each grid card is with the order: sure cards place the window, unclear ones take the name at their place.</summary>
public class CardOrderOwnersTests
{
    static readonly double[] Xs = [586, 846, 1106], Ys = [426, 543, 660];
    static MenuCard Card(string name, int slot, int? level = null) => new(name, null, Xs[slot % 3], Ys[slot / 3], 245, Ys[slot / 3] + 19, level);
    static HomeworkCharacter Char(string name, int? level = null) => new() { Name = name, ItemLevel = level, Picks = [new HomeworkPick("r", "노말")] };

    static readonly HomeworkCharacter[] Listed = [Char("너무아파요살살", 1791), Char("매우아파요살살"), Char("검지관절브레이커", 1752)];

    [Fact]
    public void Sure_cards_are_exact_names_and_the_played_one_by_slot()
    {
        var read = new MenuRead(true, "너무아파요살살", null, [Card("매우아파요살살", 1), Card("김시관절브래0P1", 2)], false, PlayingCard: Card("", 0));
        var sure = CardOrder.Sure(read, "너무아파요살살", Listed);
        Assert.Equal(2, sure.Count);
        Assert.Equal("너무아파요살살", sure[0]);
        Assert.Equal("매우아파요살살", sure[1]);
    }

    [Fact]
    public void A_name_read_on_two_cards_is_sure_on_neither()
    {
        var read = new MenuRead(true, null, null, [Card("매우아파요살살", 1), Card("매우아파요살살", 2)], false);
        Assert.Empty(CardOrder.Sure(read, null, Listed));
    }

    [Fact]
    public void An_unclear_card_takes_the_name_at_its_place_in_the_order()
    {
        var read = new MenuRead(true, "너무아파요살살", null, [Card("매우아파요살살", 1), Card("김시관절브래0P1", 2)], false, PlayingCard: Card("", 0));
        var owners = CardOrder.Owners(read, "너무아파요살살", Listed, ["너무아파요살살", "매우아파요살살", "검지관절브레이커"]);
        Assert.Equal("검지관절브레이커", owners.Single(x => x.Card.Name == "김시관절브래0P1").Owner);
        Assert.Equal("매우아파요살살", owners.Single(x => x.Card.Name == "매우아파요살살").Owner);
    }

    [Fact]
    public void Without_an_order_or_with_a_name_no_longer_listed_the_card_falls_back_to_its_name()
    {
        var read = new MenuRead(true, null, null, [Card("매우아파요살살", 1), Card("매우Ot파요살살", 2)], false);
        Assert.Equal("매우아파요살살", CardOrder.Owners(read, null, Listed, []).Single(x => x.Card.Name == "매우Ot파요살살").Owner);
        Assert.Equal("매우아파요살살", CardOrder.Owners(read, null, Listed, ["x", "매우아파요살살", "지운캐릭터"]).Single(x => x.Card.Name == "매우Ot파요살살").Owner);
    }

    [Fact]
    public void A_step_learns_and_the_same_mismatch_twice_in_a_row_rebuilds()
    {
        string[] order = ["a", "b", "c"];
        var swapped = new Dictionary<int, string> { [0] = "b", [1] = "a" };
        var once = CardOrder.Step(order, null, swapped);
        Assert.Equal(order, once.Order);
        Assert.NotNull(once.Mismatch);
        var other = CardOrder.Step(once.Order, once.Mismatch, new Dictionary<int, string> { [0] = "c", [1] = "a" });
        Assert.Equal(order, other.Order);   // another mismatch, another window: not twice the same
        var twice = CardOrder.Step(once.Order, once.Mismatch, new Dictionary<int, string> { [1] = "a", [0] = "b" });
        Assert.Equal(["b", "a", "c"], twice.Order);
        Assert.Null(twice.Mismatch);

        var learned = CardOrder.Step(["a", "", "c"], swapped, new Dictionary<int, string> { [0] = "a", [1] = "b" });
        Assert.Equal(["a", "b", "c"], learned.Order);
        Assert.Null(learned.Mismatch);
    }

    [Fact]
    public void A_card_whose_level_is_not_the_order_names_is_not_placed_by_the_order()
    {
        // A new character in a slot the order still holds for someone else (a stale order): its level tells them apart.
        var read = new MenuRead(true, "너무아파요살살", null, [Card("매우아파요살살", 1), Card("새로운캐릭터", 2, 1640)], false, PlayingCard: Card("", 0));
        var owners = CardOrder.Owners(read, "너무아파요살살", Listed, ["너무아파요살살", "매우아파요살살", "검지관절브레이커"]);
        Assert.Null(owners.Single(x => x.Card.Name == "새로운캐릭터").Owner);
        var misread = read with { Cards = [Card("매우아파요살살", 1), Card("김시관절브래0P1", 2, 1752)] };
        Assert.Equal("검지관절브레이커", CardOrder.Owners(misread, "너무아파요살살", Listed, ["너무아파요살살", "매우아파요살살", "검지관절브레이커"])
            .Single(x => x.Card.Name == "김시관절브래0P1").Owner);
    }

    [Fact]
    public void Slots_the_read_missed_get_a_card_from_the_order()
    {
        // A recalled read (a scroll, a hovered card): the strip memory knows one card; the order places the rest of the window.
        var read = new MenuRead(true, null, null, [Card("매우아파요살살", 1)], false, PlayingCard: Card("", 0));
        var owners = CardOrder.Owners(read, "너무아파요살살", Listed, ["너무아파요살살", "매우아파요살살", "검지관절브레이커", "지운캐릭터"]);
        var filled = owners.Single(x => x.Owner == "검지관절브레이커").Card;
        Assert.Equal((Xs[2], Ys[0]), (filled.X, filled.Y));
        Assert.Equal(2, owners.Count);   // not the played one's slot, not a name no longer listed, nothing past the order's end
        Assert.Single(CardOrder.Owners(read, "너무아파요살살", Listed, []));   // no order: only what was read
    }

    [Fact]
    public void The_played_card_is_placed_by_the_order_when_접속_중_is_not_read()
    {
        // No PlayingCard: "접속 중" unreadable and no single gap. The portrait says who is played; the order says where its card is.
        var read = new MenuRead(true, "너무아파요살살", null, [Card("매우아파요살살", 1), Card("검지관절브레이커", 2)], false);
        var grid = Homework.OnGrid(read, "너무아파요살살", Listed, ["너무아파요살살", "매우아파요살살", "검지관절브레이커"]);
        var played = grid.Single(x => x.Character.Name == "너무아파요살살").Card;
        Assert.Equal((Xs[0], Ys[0]), (played.X, played.Y));
        Assert.DoesNotContain(Homework.OnGrid(read, "너무아파요살살", Listed), x => x.Character.Name == "너무아파요살살");   // no order: unknown
    }

    [Fact]
    public void The_grid_tags_an_unclear_card_by_the_order()
    {
        var read = new MenuRead(true, "너무아파요살살", null, [Card("매우아파요살살", 1), Card("김시관절브래0P1", 2)], false, PlayingCard: Card("", 0));
        var grid = Homework.OnGrid(read, "너무아파요살살", Listed, ["너무아파요살살", "매우아파요살살", "검지관절브레이커"]);
        Assert.Contains(grid, x => x.Card.Name == "김시관절브래0P1" && x.Character.Name == "검지관절브레이커");
        Assert.DoesNotContain(Homework.OnGrid(read, "너무아파요살살", Listed), x => x.Card.Name == "김시관절브래0P1");
    }
}

/// <summary>The 숙제 tab's order: the game's (learned order first, the rest as added), or the one the user dragged.</summary>
public class CardOrderTabTests
{
    static readonly HomeworkCharacter A = new() { Name = "a" }, B = new() { Name = "b" }, C = new() { Name = "c" };

    [Fact]
    public void Following_the_game_puts_the_learned_order_first_and_the_rest_as_added()
        => Assert.Equal([B, A, C], CardOrder.TabOrder([A, B, C], ["b", "", "a", "지운캐릭터"], followGame: true));

    [Fact]
    public void Not_following_the_game_keeps_the_list_as_dragged()
        => Assert.Equal([A, B, C], CardOrder.TabOrder([A, B, C], ["b", "a"], followGame: false));

    [Fact]
    public void A_dragged_character_takes_the_place_of_the_one_it_is_dropped_on()
    {
        Assert.Equal([B, C, A], CardOrder.Move([A, B, C], "a", "c"));
        Assert.Equal([C, A, B], CardOrder.Move([A, B, C], "c", "a"));
        Assert.Equal([A, B, C], CardOrder.Move([A, B, C], "a", "a"));
        Assert.Equal([A, B, C], CardOrder.Move([A, B, C], "x", "a"));   // gone meanwhile
    }

    [Fact]
    public void A_character_moved_by_key_swaps_with_the_one_beside_it_and_stops_at_the_ends()
    {
        Assert.Equal([B, A, C], CardOrder.Nudge([A, B, C], "a", 1));
        Assert.Equal([B, A, C], CardOrder.Nudge([A, B, C], "b", -1));
        Assert.Equal([A, B, C], CardOrder.Nudge([A, B, C], "a", -1));   // first: nothing above
        Assert.Equal([A, B, C], CardOrder.Nudge([A, B, C], "c", 1));    // last: nothing below
        Assert.Equal([A, B, C], CardOrder.Nudge([A, B, C], "x", 1));    // gone meanwhile
    }
}

/// <summary>The order follows a rename and survives odd saved data.</summary>
public sealed class CardOrderUpkeepTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void A_rename_renames_the_order_and_a_merge_leaves_one_place()
    {
        var hw = new HomeworkSettings { Characters = [new() { Name = "a" }, new() { Name = "b" }], Order = ["a", "b"] };
        Assert.True(Homework.Rename(hw, "a", "c"));
        Assert.Equal(["c", "b"], hw.Order);
        Assert.True(Homework.Rename(hw, "c", "b"));   // merged into b
        Assert.Equal(["", "b"], hw.Order);
    }

    [Fact]
    public void A_null_order_or_null_names_load_as_empty()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"Homework":{"Order":null}}""");
        Assert.Empty(new GaugeOverlay.Core.Data.SettingsStore(path).Load(out _).Homework.Order);
        File.WriteAllText(path, """{"Homework":{"Order":["a",null]}}""");
        Assert.Equal(["a", ""], new GaugeOverlay.Core.Data.SettingsStore(path).Load(out _).Homework.Order);
    }
}
