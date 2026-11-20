using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>칸 맞춤's arrangements and drops (slot-layout.plan.md §2), written as <see cref="SlotArrangement"/> text: columns by
/// '|', a stack "s" + ids, a key run "r" + top ids '/' bottom ids (ids are <see cref="SlotId"/> numbers).</summary>
public class SlotDropTests
{
    const SlotId 각 = SlotId.각성기, 초 = SlotId.초각스, Q = SlotId.일반1, W = SlotId.일반2, E = SlotId.일반3, R = SlotId.일반4,
        A = SlotId.일반5, D = SlotId.일반7, 아1 = SlotId.아덴1;

    static string Drop(SlotId dragged, SlotTarget target, string? from = null) =>
        SlotArrangement.Format(SlotDrop.Apply(SlotArrangement.Of(from), dragged, target));

    [Fact]
    public void Default_text_round_trips()
    {
        Assert.Equal("s0|s1|r2,3,4,5/6,7,8,9|s10,11", SlotArrangement.Format(SlotGrid.Default));
        Assert.Equal(SlotArrangement.Format(SlotGrid.Default), SlotArrangement.Format(SlotArrangement.Of("s0|s1|r2,3,4,5/6,7,8,9|s10,11")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("s0|s1")]                                // boxes missing
    [InlineData("s0|s0|s1|r2,3,4,5/6,7,8,9|s10,11")]     // one twice
    [InlineData("s0|s1|r2,3,4,5/6,7,8,9|s10,99")]        // unknown id
    [InlineData("s0|s|s1|r2,3,4,5/6,7,8,9|s10,11")]      // empty stack
    [InlineData("x0|s1|r2,3,4,5/6,7,8,9|s10,11")]        // unknown column
    public void A_bad_text_is_the_default(string? text) =>
        Assert.Equal(SlotArrangement.Format(SlotGrid.Default), SlotArrangement.Format(SlotArrangement.Of(text)));

    [Fact]
    public void A_middle_drop_swaps_any_two_blocks()
    {
        Assert.Equal("s2|s1|r0,3,4,5/6,7,8,9|s10,11", Drop(각, new SlotTarget(Q, DropSide.Middle)));
        Assert.Equal("s0|s1|r2,3,4,5/6,7,8,9|s11,10", Drop(아1, new SlotTarget(SlotId.아덴2, DropSide.Middle)));
    }

    [Fact]
    public void A_key_beside_a_key_goes_into_that_row() =>
        Assert.Equal("s0|s1|r3,4,2,5/6,7,8,9|s10,11", Drop(Q, new SlotTarget(E, DropSide.Right)));   // W E Q R / A S D F

    [Fact]
    public void An_aden_box_beside_a_key_goes_into_that_row() =>
        Assert.Equal("s0|s1|r2,3,4,5/10,6,7,8,9|s11", Drop(아1, new SlotTarget(A, DropSide.Left)));

    [Fact]
    public void Awakening_beside_a_key_splits_the_run_around_it() =>
        Assert.Equal("s0|r2,3,4/6,7,8|s1|r5/9|s10,11", Drop(초, new SlotTarget(E, DropSide.Right)));   // Q W E 초 R / A S D 초 F

    [Fact]
    public void Awakening_beside_a_runs_last_key_is_a_new_column_after_it() =>
        Assert.Equal("s1|r2,3,4,5/6,7,8,9|s0|s10,11", Drop(각, new SlotTarget(R, DropSide.Right)));

    [Fact]
    public void A_stack_edge_joins_that_stack() =>
        Assert.Equal("s0,1|r2,3,4,5/6,7,8,9|s10,11", Drop(초, new SlotTarget(각, DropSide.Bottom)));

    [Fact]
    public void A_key_on_a_stacks_top_joins_it_and_leaves_its_row() =>
        Assert.Equal("s2,0|s1|r3,4,5/6,7,8,9|s10,11", Drop(Q, new SlotTarget(각, DropSide.Top)));

    [Fact]
    public void A_stack_side_is_a_new_column() =>
        Assert.Equal("s1|s0|r2,3,4,5/6,7,8,9|s10,11", Drop(각, new SlotTarget(초, DropSide.Right)));

    [Fact]
    public void A_runs_emptied_row_stays_and_an_emptied_run_goes()
    {
        string one = "s0|s1|r2/6|s3,4,5,7,8,9,10,11";
        Assert.Equal("s0|s1|r/6|s2,3,4,5,7,8,9,10,11", Drop(Q, new SlotTarget(W, DropSide.Top), one));
        Assert.Equal("s0|s1|s2,3,4,5,7,8,9,10,11,6", Drop(SlotId.일반5, new SlotTarget(SlotId.아덴2, DropSide.Bottom), "s0|s1|r/6|s2,3,4,5,7,8,9,10,11"));
    }

    [Fact]
    public void Two_runs_that_meet_join()
    {
        // 초 left the column between the two halves: they become one run again.
        Assert.Equal("s1,0|r2,3,4,5/6,7,8,9|s10,11", Drop(초, new SlotTarget(각, DropSide.Top), "s0|r2,3,4/6,7,8|s1|r5/9|s10,11"));
    }

    [Fact]
    public void Dropping_on_itself_changes_nothing() =>
        Assert.Equal("s0|s1|r2,3,4,5/6,7,8,9|s10,11", Drop(Q, new SlotTarget(Q, DropSide.Left)));

    // ── Where the cursor drops (the plan's table) ──

    static readonly Cell Key = new(100, 0, 40, 40), Tall = new(0, 0, 40, 82);

    [Theory]
    [InlineData(120, 20, DropSide.Middle)]
    [InlineData(105, 20, DropSide.Left)]    // left ¼
    [InlineData(135, 20, DropSide.Right)]
    [InlineData(120, 2, DropSide.Middle)]   // a key's top is no stack edge
    public void Key_zones(double x, double y, DropSide side) =>
        Assert.Equal(new SlotTarget(E, side), SlotDrop.TargetAt(Q, [(E, Key)], SlotArrangement.Of(null), x, y));

    [Theory]
    [InlineData(20, 41, DropSide.Middle)]
    [InlineData(20, 10, DropSide.Top)]      // top 30%
    [InlineData(20, 75, DropSide.Bottom)]
    [InlineData(4, 41, DropSide.Left)]      // left 20%
    [InlineData(37, 41, DropSide.Right)]
    public void Stack_zones(double x, double y, DropSide side) =>
        Assert.Equal(new SlotTarget(각, side), SlotDrop.TargetAt(초, [(각, Tall)], SlotArrangement.Of(null), x, y));

    [Fact]
    public void Nowhere_and_itself_are_no_target()
    {
        Assert.Null(SlotDrop.TargetAt(Q, [(E, Key)], SlotArrangement.Of(null), 300, 20));
        Assert.Null(SlotDrop.TargetAt(E, [(E, Key)], SlotArrangement.Of(null), 120, 20));
    }
}

/// <summary>The buff bars' rows (slot-layout.plan.md §2): rows by '/', bars by ',' (<see cref="BuffKind"/> numbers).</summary>
public class BarDropTests
{
    const BuffKind 공 = BuffKind.공증, 아 = BuffKind.아덴피증, 초 = BuffKind.초각스피증, 뎀 = BuffKind.뎀증, 낙 = BuffKind.낙인;

    static string Drop(BuffKind dragged, BarTarget target, string? from = null) =>
        BarArrangement.Format(BarDrop.Apply(BarArrangement.Of(from), dragged, target));

    [Fact]
    public void Default_text() => Assert.Equal("0,4/1,2/3", BarArrangement.Format(BarRows.Default));

    [Theory]
    [InlineData(null)]
    [InlineData("0,4/1,2")]
    [InlineData("0,4/1,2/3,3")]
    [InlineData("0,4//1,2/3")]
    [InlineData("0,4/1,2/9")]
    public void A_bad_text_is_the_default(string? text) => Assert.Equal("0,4/1,2/3", BarArrangement.Format(BarArrangement.Of(text)));

    [Fact]
    public void A_middle_drop_swaps() => Assert.Equal("3,4/1,2/0", Drop(공, new BarTarget(뎀, DropSide.Middle)));

    [Fact]
    public void A_bars_end_inserts_into_its_row() => Assert.Equal("0,3,4/1,2", Drop(뎀, new BarTarget(낙, DropSide.Left)));   // 공증 뎀증 낙인 / 아덴 초각

    [Fact]
    public void Past_a_bars_long_side_is_a_new_row()
    {
        Assert.Equal("4/0/1,2/3", Drop(공, new BarTarget(낙, DropSide.Bottom)));
        Assert.Equal("0,4/2/1/3", Drop(초, new BarTarget(아, DropSide.Top)));
    }

    static readonly Cell Bar = new(0, 0, 100, 10);

    [Theory]
    [InlineData(50, 5, DropSide.Middle)]
    [InlineData(10, 5, DropSide.Left)]
    [InlineData(95, 5, DropSide.Right)]
    [InlineData(50, -1, DropSide.Top)]     // in the gap above
    [InlineData(50, 11, DropSide.Bottom)]
    public void Bar_zones(double x, double y, DropSide side) =>
        Assert.Equal(new BarTarget(낙, side), BarDrop.TargetAt(공, [(낙, Bar)], 2, x, y));

    [Fact]
    public void A_bar_under_the_cursor_wins_over_the_gap_reach_of_the_bar_above()
    {
        // rows 1 apart, reach 8: the point is in 아's bottom reach but on 낙 itself
        Cell above = new(0, 0, 100, 10), below = new(0, 11, 100, 10);
        Assert.Equal(new BarTarget(낙, DropSide.Left), BarDrop.TargetAt(공, [(아, above), (낙, below)], 8, 2, 14));
        Assert.Equal(new BarTarget(아, DropSide.Bottom), BarDrop.TargetAt(공, [(아, above), (낙, below)], 8, 50, 10.5));
    }

    [Fact]
    public void Far_from_a_bar_is_no_target() => Assert.Null(BarDrop.TargetAt(공, [(낙, Bar)], 2, 50, 20));
}
