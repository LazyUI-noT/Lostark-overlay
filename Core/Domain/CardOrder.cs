using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>The 다른 캐릭터 접속 grid's card order (card-order.plan.md): each character's card keeps its place in the roster's
/// order whatever is picked, the grid scrolls by rows, the player reorders rarely (user, 2026-11-08). The order is the names by
/// place, "" for a place not learned yet. A read's sure cards (slot → name: an exact name, a saved level with a near name, the
/// played one's 접속 중 card) find the window and teach the order; an unclear card takes the name at its place. Pure.</summary>
public static class CardOrder
{
    /// <summary>Cards per grid row: a scroll moves the window by whole rows (one row per step assumed until another player
    /// confirms, board 2026-11-08).</summary>
    public const int RowCards = 3;

    /// <summary>Where the visible window starts in <paramref name="order"/>: the whole-row offset (up to one row past the end, for a
    /// new card) where the most sure cards agree and none disagrees; null with no sure card, no agreeing window or two alike. With
    /// no order yet the window is the top. <paramref name="scrollRows"/>: the scrollbar's read, when there is one (not read yet:
    /// rosters long enough to scroll more than once would want it, user 2026-11-08).</summary>
    public static int? Offset(IReadOnlyList<string> order, IReadOnlyDictionary<int, string> sure, int? scrollRows = null)
    {
        if (scrollRows is { } rows) return rows * RowCards;
        if (sure.Count == 0) return null;
        if (order.Count == 0) return 0;
        var fits = Windows(order).Select(o => (Offset: o, Score: Score(order, o, sure))).Where(x => x.Score.Disagree == 0 && x.Score.Agree > 0)
            .OrderByDescending(x => x.Score.Agree).ToList();
        if (fits.Count == 0) return null;
        return fits.Count > 1 && fits[1].Score.Agree == fits[0].Score.Agree ? null : fits[0].Offset;
    }

    /// <summary>The name at <paramref name="slot"/> of the window at <paramref name="offset"/>; null past the end or not learned.</summary>
    public static string? Owner(IReadOnlyList<string> order, int offset, int slot) =>
        offset + slot < order.Count && order[offset + slot] is { Length: > 0 } name ? name : null;

    /// <summary>The order with the sure cards at their places (a name found elsewhere moves: one place per name); a new list.</summary>
    public static IReadOnlyList<string> Learn(IReadOnlyList<string> order, int offset, IReadOnlyDictionary<int, string> sure)
    {
        var names = sure.Values.ToHashSet();
        var learned = order.Select(n => names.Contains(n) ? "" : n).ToList();
        foreach (var (slot, name) in sure)
        {
            while (learned.Count <= offset + slot) learned.Add("");
            learned[offset + slot] = name;
        }
        while (learned.Count > 0 && learned[^1].Length == 0) learned.RemoveAt(learned.Count - 1);
        return learned;
    }

    /// <summary>The read fits no window though two sure cards say where they are: the player reordered the cards (or two reads
    /// misled alike). The caller rebuilds once it holds twice in a row.</summary>
    public static bool Mismatch(IReadOnlyList<string> order, IReadOnlyDictionary<int, string> sure) =>
        order.Count > 0 && sure.Count >= 2 && Windows(order).All(o => Score(order, o, sure).Disagree > 0);

    /// <summary>An order learned again from this read, taken as the top of the grid (a later read elsewhere corrects it); the
    /// names not in it follow in their old order.</summary>
    public static IReadOnlyList<string> Rebuild(IReadOnlyList<string> order, IReadOnlyDictionary<int, string> sure)
    {
        var top = Learn([], 0, sure);
        return [.. top, .. order.Where(n => n.Length > 0 && !sure.Values.Contains(n))];
    }

    /// <summary>A read's sure cards by slot (<see cref="MenuSlots.SlotOf"/>): a card <see cref="Homework.SureOwner"/> owns, never the
    /// played one (its card shows 접속 중), and the 접속 중 card as <paramref name="current"/>. A name sure on two cards is on neither.</summary>
    public static IReadOnlyDictionary<int, string> Sure(MenuRead read, string? current, IReadOnlyList<HomeworkCharacter> characters)
    {
        var others = characters.Where(c => c.Name != current).ToList();
        var cards = read.Cards.Select(c => (Slot: MenuSlots.SlotOf(c), Name: Homework.SureOwner(c, others)));
        if (read.PlayingCard is { } playing && current is not null) cards = cards.Append((MenuSlots.SlotOf(playing), current));
        var placed = cards.Where(x => x.Slot is not null && x.Name is not null).Select(x => (Slot: x.Slot!.Value, Name: x.Name!)).ToList();
        return placed.GroupBy(x => x.Name).Where(g => g.Count() == 1).Select(g => g.First()).GroupBy(x => x.Slot).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Name);
    }

    /// <summary>Whose each of the read's cards is (not the played card): sure, else the order's name at its place when listed, not
    /// the played one and the card's name or level fits it, else <see cref="Homework.CardOwner"/>. A slot the read has no card in (a recalled read knows only some; a
    /// hovered card reads as nothing) gets one for the order's name there. Without a window (<see cref="Offset"/>) the order isn't used.</summary>
    public static IReadOnlyList<(MenuCard Card, string? Owner)> Owners(MenuRead read, string? current, IReadOnlyList<HomeworkCharacter> characters, IReadOnlyList<string> order)
    {
        var others = characters.Where(c => c.Name != current).ToList();
        var sure = Sure(read, current, characters);
        int? offset = order.Count > 0 ? Offset(order, sure) : null;
        string? ByOrder(int? slot) => offset is { } o && slot is { } s && Owner(order, o, s) is { } name && others.Any(c => c.Name == name) ? name : null;
        // A card read can still be someone else than the order holds (a new character in a stale order's place): its name near the
        // order's, or its level fitting that one's saved level (or either unknown), says it is that one.
        bool Fits(MenuCard card, string name) => CharacterName.Near(card.Name, [name]).Count > 0
            || others.First(c => c.Name == name).ItemLevel is not { } saved || Homework.Fit(saved, card.ItemLevel) < Homework.NoFit;
        string? Placed(MenuCard card) => ByOrder(MenuSlots.SlotOf(card)) is { } name && Fits(card, name) ? name : null;
        var byRead = read.Cards.Select(card => (card, Owner: Homework.SureOwner(card, others) ?? Placed(card) ?? Homework.CardOwner(card, others)));
        var taken = read.Cards.Append(read.PlayingCard).Select(c => c is null ? null : MenuSlots.SlotOf(c)).ToHashSet();
        var filled = Enumerable.Range(0, MenuSlots.Columns * MenuSlots.Rows).Where(s => !taken.Contains(s))
            .Select(s => (Slot: s, Owner: ByOrder(s))).Where(x => x.Owner is not null)
            .Select(x => (card: MenuSlots.Placed(new MenuCard(x.Owner!, null, 0, 0, GameMenu.CardWidth, 0), x.Slot), x.Owner));
        return [.. byRead.Concat(filled)];
    }

    /// <summary>The played character's card when the read didn't find it ("접속 중" unreadable, no single gap): at
    /// <paramref name="current"/>'s place in the order (the portrait says who is played), when the window is known and that slot holds
    /// no other card; else null.</summary>
    public static MenuCard? PlayingCard(MenuRead read, string? current, IReadOnlyList<HomeworkCharacter> characters, IReadOnlyList<string> order)
    {
        if (current is null || order.Count == 0 || Offset(order, Sure(read, current, characters)) is not { } offset) return null;
        int slot = order.ToList().IndexOf(current) - offset;
        if (slot < 0 || slot >= MenuSlots.Columns * MenuSlots.Rows || read.Cards.Any(c => MenuSlots.SlotOf(c) == slot)) return null;
        return MenuSlots.Placed(new MenuCard("", null, 0, 0, GameMenu.CardWidth, 0), slot);
    }

    /// <summary>The order after a read, and the read's sure cards when they fit no window (<see cref="Mismatch"/>): learned where the
    /// read fits a window; the same mismatch twice in a row (the same cards in the same slots: the player reordered, user 2026-11-08)
    /// rebuilds it from this read; a read placing nothing changes nothing. A rebuild takes the read as the top: a later read at the
    /// real top mismatches it and rebuilds again.</summary>
    public static (IReadOnlyList<string> Order, IReadOnlyDictionary<int, string>? Mismatch) Step(IReadOnlyList<string> order,
        IReadOnlyDictionary<int, string>? lastMismatch, IReadOnlyDictionary<int, string> sure)
    {
        if (Mismatch(order, sure))
            return lastMismatch is not null && lastMismatch.Count == sure.Count && sure.All(x => lastMismatch.GetValueOrDefault(x.Key) == x.Value)
                ? (Rebuild(order, sure), null) : (order, sure);
        return Offset(order, sure) is { } offset ? (Learn(order, offset, sure), null) : (order, lastMismatch);
    }

    /// <summary>The 숙제 tab's order: following the game, the characters in <paramref name="order"/> first, then the rest as added;
    /// else the list as the user dragged it (card-order.plan.md, 「게임 순서 따르기」).</summary>
    public static IReadOnlyList<HomeworkCharacter> TabOrder(IReadOnlyList<HomeworkCharacter> characters, IReadOnlyList<string> order, bool followGame)
    {
        if (!followGame) return characters;
        var place = order.Select((name, i) => (name, i)).Where(x => x.name.Length > 0).DistinctBy(x => x.name).ToDictionary(x => x.name, x => x.i);
        return [.. characters.OrderBy(c => place.GetValueOrDefault(c.Name, int.MaxValue))];   // stable: the rest keep the order they were added in
    }

    /// <summary><paramref name="list"/> with <paramref name="name"/> moved to <paramref name="onto"/>'s place; a new list.</summary>
    public static List<HomeworkCharacter> Move(IReadOnlyList<HomeworkCharacter> list, string name, string onto)
    {
        var moved = list.ToList();
        int from = moved.FindIndex(c => c.Name == name), to = moved.FindIndex(c => c.Name == onto);
        if (from < 0 || to < 0 || from == to) return moved;
        var c = moved[from];
        moved.RemoveAt(from);
        moved.Insert(to, c);
        return moved;
    }

    /// <summary>≡ by keys (Alt+↑/↓): <paramref name="name"/> swaps with the character <paramref name="by"/> places away; at an end, or
    /// gone meanwhile, the list stays as it is. A new list.</summary>
    public static List<HomeworkCharacter> Nudge(IReadOnlyList<HomeworkCharacter> list, string name, int by)
    {
        int from = list.ToList().FindIndex(c => c.Name == name), to = from + by;
        return from < 0 || to < 0 || to >= list.Count ? [.. list] : Move(list, name, list[to].Name);
    }

    static IEnumerable<int> Windows(IReadOnlyList<string> order)
    {
        for (int o = 0; o <= order.Count; o += RowCards) yield return o;
    }

    /// <summary>Sure cards the window at <paramref name="offset"/> agrees with, and those it can't hold: another name at the card's
    /// place, or the card's name learned at another place.</summary>
    static (int Agree, int Disagree) Score(IReadOnlyList<string> order, int offset, IReadOnlyDictionary<int, string> sure)
    {
        int agree = 0, disagree = 0;
        foreach (var (slot, name) in sure)
        {
            string? there = offset + slot < order.Count ? order[offset + slot] : null;
            if (there == name) agree++;
            else if (there is { Length: > 0 } || order.Contains(name)) disagree++;
        }
        return (agree, disagree);
    }
}
