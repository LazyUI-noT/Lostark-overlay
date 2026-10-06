using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Skills placed anywhere: boxes that stand apart get plates of their own (<see cref="FreeLayout.Plates"/>), a
/// party member's spread layout is drawn compact, and 쿨타임 중 숨기기 hides a box while a read cooldown runs.</summary>
public class SpreadLayoutTests
{
    const double Pad = FreeLayout.PlatePad;
    static readonly TimeSpan Now = TimeSpan.FromSeconds(100);

    // ── plates ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_default_grid_is_one_plate()
    {
        var cells = FreeLayout.Default(8).Values.ToList();
        var plates = FreeLayout.Plates(cells, Pad);
        Assert.Single(plates);
        Assert.Equal(cells.Count, plates[0].Members.Count);
    }

    [Fact]
    public void A_box_far_from_the_rest_gets_its_own_plate()
    {
        Cell a = new(0, 0, 34, 34), b = new(36, 0, 34, 34), far = new(300, 200, 34, 34);
        var plates = FreeLayout.Plates([a, b, far], Pad);

        Assert.Equal(2, plates.Count);
        Assert.Equal([0, 1], plates[0].Members);
        Assert.Equal([2], plates[1].Members);
        Assert.Equal(new Cell(300 - Pad, 200 - Pad, 34 + 2 * Pad, 34 + 2 * Pad), plates[1].Plate);
    }

    /// <summary>Closer than two pads the plates would overlap (and darken twice): they become one.</summary>
    [Fact]
    public void Boxes_closer_than_two_pads_share_a_plate_and_plates_never_overlap()
    {
        Cell a = new(0, 0, 10, 10), near = new(10 + 2 * Pad - 1, 0, 10, 10), apart = new(10 + 2 * Pad, 0, 10, 10);
        Assert.Single(FreeLayout.Plates([a, near], Pad));
        Assert.Equal(2, FreeLayout.Plates([a, apart], Pad).Count);
    }

    /// <summary>A merged plate grows; a box that its bigger plate now overlaps joins too.</summary>
    [Fact]
    public void Merging_repeats_until_no_plates_overlap()
    {
        Cell a = new(0, 0, 10, 10), b = new(12, 0, 10, 40), c = new(-20, 45, 10, 10);
        var plates = FreeLayout.Plates([a, b, c], Pad);
        for (int i = 0; i < plates.Count; i++)
            for (int j = i + 1; j < plates.Count; j++)
                Assert.False(Overlap(plates[i].Plate, plates[j].Plate));
    }

    [Fact]
    public void No_boxes_no_plates() => Assert.Empty(FreeLayout.Plates([], Pad));

    static bool Overlap(Cell a, Cell b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    // ── a member's spread layout ──────────────────────────────────────────────────────

    [Fact]
    public void A_layout_is_spread_when_its_skill_boxes_need_two_plates()
    {
        var grid = FreeLayout.Default(8);
        Assert.False(FreeLayout.Spread(grid));
        var moved = new Dictionary<Part, Cell>(grid) { [Part.각성기] = grid[Part.각성기] with { X = 500, Y = -300 } };
        Assert.True(FreeLayout.Spread(moved));
    }

    /// <summary>Buff bars aren't drawn for party members, so a buff bar alone doesn't make the layout spread.</summary>
    [Fact]
    public void A_far_buff_bar_does_not_spread_a_layout()
    {
        var grid = FreeLayout.Default(8);
        var moved = new Dictionary<Part, Cell>(grid) { [Part.버프공증] = grid[Part.버프공증] with { X = 900 } };
        Assert.False(FreeLayout.Spread(moved));
    }

    [Fact]
    public void A_spread_received_layout_is_drawn_compact()
    {
        var s = new Settings();
        s.Party.ReceiveLayout = true;
        var grid = FreeLayout.Default(8);
        var close = new Dictionary<Part, Cell>(grid) { [Part.초각스] = grid[Part.초각스] with { Y = grid[Part.초각스].Y + 1 } };
        var spread = new Dictionary<Part, Cell>(grid) { [Part.각성기] = grid[Part.각성기] with { X = 500 } };
        var colors = new Dictionary<SlotGroup, string>();

        Assert.Same(close, ClassProfiles.Member(s, "워로드", Role.딜러, new Look(colors, Layout: close)).Layout);
        Assert.Null(ClassProfiles.Member(s, "워로드", Role.딜러, new Look(colors, Layout: spread)).Layout);
    }

    // ── 쿨타임 중 숨기기 ────────────────────────────────────────────────────────────────

    static SlotState Cooling(bool estimated = false, bool stacked = false, TimeSpan? ends = null) =>
        new(SlotId.일반1, SlotPhase.Cooldown, ends ?? Now + TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), estimated, Now - TimeSpan.FromSeconds(5), Stacked: stacked);

    [Fact]
    public void A_read_cooldown_hides_the_box()
    {
        Assert.True(CooldownLook.Hides(Cooling(), Now, held: false));
        Assert.True(CooldownLook.Hides(new SlotState(SlotId.일반1, SlotPhase.LongCooldown, null, null, false, Now, Minutes: 3), Now, held: false));
    }

    [Fact]
    public void Ready_and_unsure_states_show_the_box()
    {
        Assert.False(CooldownLook.Hides(new SlotState(SlotId.일반1, SlotPhase.Ready, null, null, false, Now), Now, held: false));
        Assert.False(CooldownLook.Hides(SlotState.Unknown(SlotId.일반1), Now, held: false));
        Assert.False(CooldownLook.Hides(new SlotState(SlotId.일반1, SlotPhase.Unavailable, null, null, false, Now), Now, held: false));
        Assert.False(CooldownLook.Hides(Cooling(estimated: true), Now, held: false));
    }

    [Fact]
    public void A_stack_skill_with_a_use_left_shows()
    {
        Assert.False(CooldownLook.Hides(Cooling(stacked: true), Now, held: false));
    }

    /// <summary>The cooldown has run out but the game hasn't shown Ready yet (the 99% hold): it's back already.</summary>
    [Fact]
    public void A_cooldown_that_has_run_out_shows()
    {
        Assert.False(CooldownLook.Hides(Cooling(ends: Now), Now, held: false));
    }

    /// <summary>사용 후 3초 유지: the held count shows first, then the box goes.</summary>
    [Fact]
    public void The_held_count_keeps_the_box_until_it_ends()
    {
        Assert.False(CooldownLook.Hides(Cooling(), Now, held: true));
    }

    [Fact]
    public void Hiding_is_set_per_skill_or_for_the_group()
    {
        var m = new MineProfile();
        Assert.False(m.Hides(SlotId.일반1, SlotGroup.일반스킬));

        BoxOptions.SetHideOnCooldown(m, SlotId.일반1, SlotGroup.일반스킬, true, onlyThis: false, []);
        BoxOptions.SetHideOnCooldown(m, SlotId.일반2, SlotGroup.일반스킬, false, onlyThis: true, []);
        Assert.True(m.Hides(SlotId.일반1, SlotGroup.일반스킬));
        Assert.False(m.Hides(SlotId.일반2, SlotGroup.일반스킬));
        Assert.False(m.Hides(SlotId.각성기, SlotGroup.각성기));

        BoxOptions.SetHideOnCooldown(m, SlotId.일반1, SlotGroup.일반스킬, false, onlyThis: false, []);
        Assert.False(m.Hides(SlotId.일반2, SlotGroup.일반스킬));   // the group switch clears the box set alone
    }

    [Fact]
    public void Hiding_is_copied_with_the_effects()
    {
        var from = new MineProfile();
        BoxOptions.SetHideOnCooldown(from, SlotId.초각스, SlotGroup.초각스, true, onlyThis: false, []);
        BoxOptions.SetHideOnCooldown(from, SlotId.일반3, SlotGroup.일반스킬, true, onlyThis: true, []);
        var to = new MineProfile();
        to.CopyEffects(from);
        Assert.True(to.Hides(SlotId.초각스, SlotGroup.초각스));
        Assert.True(to.Hides(SlotId.일반3, SlotGroup.일반스킬));
        from.HideOnCooldown.Clear();
        Assert.True(to.Hides(SlotId.초각스, SlotGroup.초각스));   // a copy, not shared
    }
}
