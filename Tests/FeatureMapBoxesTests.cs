using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 표시 map's boxes and arrows (mockup v59): one list for the map page and the rail.</summary>
public class FeatureMapBoxesTests
{
    [Fact]
    public void Every_arrow_joins_two_boxes_and_every_inner_box_has_its_frame()
    {
        var ids = FeatureMap.Boxes.Select(b => b.Id).ToHashSet();
        Assert.Equal(FeatureMap.Boxes.Count, ids.Count);
        Assert.All(FeatureMap.Edges, e => { Assert.Contains(e.From, ids); Assert.Contains(e.To, ids); });
        Assert.All(FeatureMap.Boxes.Where(b => b.Inside is not null), b => Assert.Contains(b.Inside!, ids));
    }

    [Fact]
    public void Every_box_is_in_one_of_the_map_colours() =>
        Assert.All(FeatureMap.Boxes, b => Assert.Contains(b.Color, FeatureMap.Colours));

    [Fact]
    public void The_homework_box_works_without_the_timeline_and_says_so()
    {
        Assert.True(FeatureMap.Edges.Single(e => e is { From: "rec", To: "hw" }).Optional);
        Assert.NotNull(FeatureMap.Boxes.Single(b => b.Id == "hw").Note);
    }

    [Fact]
    public void Daenantu_s_switch_only_hides_it_and_homework_needs_its_read()
    {
        // user, 2026-11-09: the number is read with the switch off; 숙제 holds its bar-gone check through 대난투.
        Assert.Equal([["켜기", "표시 안 함"]], FeatureMap.Boxes.Single(b => b.Id == "shadow").Choices);
        Assert.Contains(FeatureMap.Edges, e => e is { From: "shadow", To: "hw" });
    }

    [Fact]
    public void The_party_only_reaches_the_timeline_through_the_meter()
    {
        Assert.DoesNotContain(FeatureMap.Edges, e => e.From == "party" && e.To == "rec");
        Assert.Contains(FeatureMap.Edges, e => e is { From: "party", To: "meter", Party: true });
    }

    [Fact]
    public void A_box_shows_the_state_of_its_switch()
    {
        var s = new Settings();
        Assert.Equal(BoxState.On, FeatureMap.State(s, "skill"));
        s.Overlay.SkillRead = false;
        Assert.Equal(BoxState.Off, FeatureMap.State(s, "skill"));
        s.Meter.SetMode(ThreeWay.표시안함);
        Assert.Equal(BoxState.Hidden, FeatureMap.State(s, "meter"));
        Assert.Null(FeatureMap.State(s, "game"));   // a frame, no switch of its own yet
    }

    static MapEdge Line(string from, string to) => FeatureMap.Edges.Single(e => e.Party && e.From == from && e.To == to);
    static bool Cut(Settings s, string from, string to) => FeatureMap.Cut(s, Line(from, to));

    [Fact]
    public void The_meters_own_line_to_the_timeline_never_cuts()
    {
        var s = new Settings();
        Assert.False(FeatureMap.Cut(s, FeatureMap.Edges.Single(e => !e.Party && e.From == "meter" && e.To == "rec")));
        Assert.True(Cut(s, "meter", "rec"));   // 파티원 전분: not sending, so not receiving
    }

    [Fact]
    public void Cutting_a_party_line_on_the_map_sets_its_switch_and_reconnecting_brings_it_back()
    {
        var s = new Settings();
        s.Meter.Send = true;
        foreach (var e in FeatureMap.Edges.Where(e => e.Party))
        {
            FeatureMap.SetCut(s, e, true);
            Assert.True(FeatureMap.Cut(s, e));
            FeatureMap.SetCut(s, e, false);
            Assert.False(FeatureMap.Cut(s, e));
        }
    }

    [Fact]
    public void Cutting_my_figures_cuts_both_lines_and_reconnecting_brings_both_back()
    {
        var s = new Settings();
        (s.Meter.Send, s.Meter.Receive) = (true, false);   // theirs cut on its own earlier
        FeatureMap.SetCut(s, Line("meter", "party"), true);
        Assert.True(Cut(s, "meter", "party"));
        Assert.True(Cut(s, "party", "meter"));
        FeatureMap.SetCut(s, Line("meter", "party"), false);
        Assert.False(Cut(s, "meter", "party"));
        Assert.False(Cut(s, "party", "meter"));
    }

    [Fact]
    public void Cutting_their_figures_leaves_mine_going_out()
    {
        var s = new Settings();
        s.Meter.Send = true;
        FeatureMap.SetCut(s, Line("party", "meter"), true);
        Assert.True(Cut(s, "party", "meter"));
        Assert.False(Cut(s, "meter", "party"));
    }

    [Fact]
    public void The_skill_count_is_an_optional_line_that_lights_nothing_on_the_meter()
    {
        var e = FeatureMap.Edges.Single(x => x is { From: "skill", To: "meter" });
        Assert.True(e.Optional);
        var board = new Dictionary<string, Detection> { ["skill"] = new(DetectMark.Concrete, TimeSpan.Zero) };
        Assert.False(FeatureMap.Lit(FeatureMap.Edges.Single(x => x is { From: "meter", To: "rec", Party: false }), board));
    }

    [Fact]
    public void Each_party_arrow_stops_with_its_switch()
    {
        var s = new Settings();
        s.Meter.Send = true;
        Assert.False(Cut(s, "meter", "party"));
        Assert.False(Cut(s, "party", "meter"));
        s.Meter.Receive = false;
        Assert.True(Cut(s, "party", "meter"));
        s.Meter.Send = false;
        Assert.True(Cut(s, "meter", "party"));
        s.Party.SendGauge = false;
        Assert.True(Cut(s, "aden", "party"));
        Assert.False(Cut(s, "skill", "party"));
        foreach (var g in Enum.GetValues<SlotGroup>()) s.Party.Send[g] = false;
        Assert.True(Cut(s, "skill", "party"));
    }

    static Dictionary<string, Detection> Marked(string id, DetectMark mark) => new() { [id] = new Detection(mark, TimeSpan.Zero) };

    [Fact]
    public void A_failed_reader_sends_lights_back_to_each_game_setting_it_uses()
    {
        var back = FeatureMap.Edges.Where(e => FeatureMap.FlowsBack(e, Marked("amsu", DetectMark.Failed))).Select(e => e.From);
        Assert.Equal(["g-hud", "g-debuff"], back);
    }

    [Theory]
    [InlineData(DetectMark.Concrete)]
    [InlineData(DetectMark.Waiting)]
    public void A_reader_that_is_not_failing_sends_nothing_back(DetectMark mark) =>
        Assert.DoesNotContain(FeatureMap.Edges, e => FeatureMap.FlowsBack(e, Marked("amsu", mark)));

    [Fact]
    public void A_failed_box_sends_nothing_back_along_lines_that_do_not_start_at_a_game_setting() =>
        Assert.DoesNotContain(FeatureMap.Edges, e => FeatureMap.FlowsBack(e, Marked("rec", DetectMark.Failed)));

    [Fact]
    public void Ports_stand_apart_between_groups_and_centre_on_the_side() =>
        Assert.Equal([-16.0, -7.0, 7.0, 16.0], FeatureMap.SpreadPorts([0, 0, 1, 1], 9, 14, 50));

    [Fact]
    public void Ports_draw_closer_when_they_would_not_fit() =>
        Assert.Equal([-12.0, 0.0, 12.0], FeatureMap.SpreadPorts([0, 1, 2], 6, 14, 24));

    [Fact]
    public void A_lone_port_is_in_the_middle() => Assert.Equal([0.0], FeatureMap.SpreadPorts([2], 6, 14, 24));

    [Fact]
    public void A_certain_reader_lights_its_lines_and_the_game_settings_feeding_it()
    {
        var lit = FeatureMap.Edges.Where(e => FeatureMap.Lit(e, Marked("amsu", DetectMark.Concrete))).Select(e => (e.From, e.To));
        Assert.Equal([("g-hud", "amsu"), ("g-debuff", "amsu"), ("amsu", "amsuline")], lit);
    }

    [Theory]
    [InlineData(DetectMark.Waiting)]
    [InlineData(DetectMark.Failed)]
    public void An_uncertain_reader_lights_nothing(DetectMark mark) =>
        Assert.DoesNotContain(FeatureMap.Edges, e => FeatureMap.Lit(e, Marked("amsu", mark)));

    [Fact]
    public void A_box_without_a_reader_passes_on_the_lights_of_the_reader_feeding_it() =>
        Assert.True(FeatureMap.Lit(FeatureMap.Edges.Single(e => e is { From: "meter", To: "rec", Party: false }), Marked("g-jeonbun", DetectMark.Concrete)));

    [Fact]
    public void Party_lines_carry_no_lights() =>
        Assert.DoesNotContain(FeatureMap.Edges, e => e.Party && FeatureMap.Lit(e, Marked("skill", DetectMark.Concrete)));

    [Fact]
    public void Lines_to_and_from_the_party_block_go_by_ports_except_the_one_to_its_windows()
    {
        var ported = FeatureMap.Edges.Where(FeatureMap.UsesPorts).Select(e => (e.From, e.To));
        Assert.Equal([("skill", "party"), ("aden", "party"), ("meter", "party"), ("party", "meter")], ported);
    }

    [Fact]
    public void Only_lines_touching_the_party_block_cut_on_the_map()
    {
        Assert.False(FeatureMap.Cuttable(Line("meter", "rec")));   // cut on party > meter, the same switch
        Assert.True(FeatureMap.Cuttable(Line("party", "peers")));
        Assert.True(FeatureMap.Cuttable(Line("skill", "party")));
        Assert.False(FeatureMap.Cuttable(FeatureMap.Edges.First(e => !e.Party)));
    }

    [Theory]
    [InlineData(false, false, PartyLineLook.Pink)]
    [InlineData(false, true, PartyLineLook.GreySlashed)]
    [InlineData(true, false, PartyLineLook.Grey)]
    [InlineData(true, true, PartyLineLook.Pink)]
    public void A_party_line_shows_what_a_click_would_make_it(bool cut, bool hovered, PartyLineLook look) =>
        Assert.Equal(look, FeatureMap.LookOf(cut, hovered));

    [Theory]
    [InlineData("skill", true)]
    [InlineData("party", true)]
    [InlineData("aden", false)]
    [InlineData(null, false)]
    public void A_long_party_line_shows_only_with_a_box_at_either_end_in_focus(string? focus, bool shown) =>
        Assert.Equal(shown, FeatureMap.LinkShown(Line("skill", "party"), focus, null));

    [Fact]
    public void Every_overlay_window_has_a_box_for_its_hover_gif() =>
        Assert.Equal(Enum.GetValues<OverlayPart>().Order(), FeatureMap.Boxes.Select(b => FeatureMap.WindowOf(b.Id)).OfType<OverlayPart>().Order());

    [Fact]
    public void A_long_party_line_shows_while_its_own_line_is_hovered()
    {
        Assert.True(FeatureMap.LinkShown(Line("skill", "party"), null, Line("skill", "party")));
        Assert.False(FeatureMap.LinkShown(Line("skill", "party"), null, Line("aden", "party")));
    }
}
