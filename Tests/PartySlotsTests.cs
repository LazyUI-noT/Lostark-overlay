using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Which party member goes in which party window (slot 0–2): join order, pinned names, and a supporter pinned
/// to one slot.</summary>
public class PartySlotsTests
{
    static Player P(string name) => new(name, Snapshot.Empty(TimeSpan.Zero));
    static readonly Func<Player, bool> NoSupport = _ => false;

    static List<string?> Names(Player?[] slots) => [.. slots.Select(p => p?.Name)];   // not an array: Assert.Equal's span overload rejects string?

    [Fact]
    public void Without_pins_members_fill_the_slots_in_join_order() =>
        Assert.Equal(["a", "b", null], Names(PartySlots.Assign([P("a"), P("b")], [], -1, NoSupport)));

    [Fact]
    public void A_pinned_name_takes_its_slot_and_the_rest_fill_around_it() =>
        Assert.Equal(["b", "c", "a"], Names(PartySlots.Assign([P("a"), P("b"), P("c")], ["b", null, "a"], -1, NoSupport)));

    [Fact]
    public void Swapping_two_slots_swaps_their_members()
    {
        var members = new[] { P("a"), P("b"), P("c") };
        var now = Names(PartySlots.Assign(members, [], -1, NoSupport));

        var pins = PartySlots.Swap(now, 0, 1);

        Assert.Equal(["b", "a", "c"], Names(PartySlots.Assign(members, pins, -1, NoSupport)));
    }

    [Fact]
    public void A_pin_for_someone_not_here_is_skipped() =>
        Assert.Equal(["a", "b", null], Names(PartySlots.Assign([P("a"), P("b")], ["x"], -1, NoSupport)));

    [Fact]
    public void The_first_supporter_goes_to_the_support_slot_and_nobody_else_moves_for_it()
    {
        Func<Player, bool> support = p => p.Name is "bard" or "paladin";
        var members = new[] { P("a"), P("bard"), P("paladin") };

        var slots = PartySlots.Assign(members, [], supportSlot: 2, support);

        Assert.Equal(["a", "paladin", "bard"], Names(slots));
        Assert.Equal(Names(slots), Names(PartySlots.Assign(members, [], 2, support)));   // same input, same answer
    }

    [Fact]
    public void The_support_slot_beats_a_name_pinned_to_it() =>
        Assert.Equal(["x", "b", "a"], Names(PartySlots.Assign([P("x"), P("a"), P("b")], [null, "a"], 1, p => p.Name == "b")));

    [Theory]
    [InlineData(1, new[] { 2, 3, 4 })]
    [InlineData(2, new[] { 1, 3, 4 })]
    [InlineData(4, new[] { 1, 2, 3 })]
    public void Members_take_the_numbers_you_dont_in_order(int mine, int[] theirs)
    {
        Assert.Equal(theirs, PartySlots.Numbers(mine));
        Assert.Equal(theirs[1].ToString(), PartySlots.Label(1, mine));
    }

    [Fact]
    public void A_number_outside_1_to_4_counts_as_1() => Assert.Equal([2, 3, 4], PartySlots.Numbers(9));

    [Fact]
    public void The_fixed_support_number_becomes_the_slot_that_shows_that_number()
    {
        var party = new PartySettings { MyNumber = 2, SupportFixed = true, SupportNumber = 3 };
        Assert.Equal(1, PartySlots.SupportSlot(party, iAmSupport: false));   // numbers 1, 3, 4: 3 is the second
        Assert.Equal(-1, PartySlots.SupportSlot(party, iAmSupport: true));   // off while you are the supporter
        party.SupportNumber = 2;
        Assert.Equal(-1, PartySlots.SupportSlot(party, iAmSupport: false));  // your own number
        (party.SupportNumber, party.SupportFixed) = (3, false);
        Assert.Equal(-1, PartySlots.SupportSlot(party, iAmSupport: false));
    }
}
