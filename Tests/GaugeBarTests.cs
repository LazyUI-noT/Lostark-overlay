using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 아덴 게이지 bar (PLAN §4): the user's split marks, segment colours and glow per 클래스·직업각인, over the
/// classes.json default; which segments glow.</summary>
public class GaugeBarTests
{
    static readonly GaugeRule Sections = new([0.25, 0.5, 0.75], new GlowRule(GlowWhen.Sections));
    static readonly GaugeRule SlotReady = new([0.5], new GlowRule(GlowWhen.SlotReady, SlotId.아덴2, 0.5));

    [Fact]
    public void With_nothing_set_the_bar_is_the_class_default()
    {
        var bar = GaugeBar.Resolve(new Settings(), "바드", "절실한구원", Sections);
        Assert.Equal([0.25, 0.5, 0.75], bar.Splits);
        Assert.Equal(Sections.Glow, bar.Glow);
        Assert.Equal([(0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1)], bar.Segments);
        Assert.All(bar.SegmentColors, c => Assert.Equal(new Settings().Gauge.Fill, c));
    }

    [Fact]
    public void The_users_splits_and_colours_replace_the_default_for_that_class_only()
    {
        var s = new Settings();
        var mine = GaugeBar.Edit(s, "바드", "절실한구원");
        mine.Splits = [0.5];
        mine.UnifiedColor = false;
        mine.SegmentColors = ["#FF0000"];   // the second segment has none: the fill colour

        var bar = GaugeBar.Resolve(s, "바드", "절실한구원", Sections);
        Assert.Equal([0.5], bar.Splits);
        Assert.Equal(["#FF0000", s.Gauge.Fill], bar.SegmentColors);
        Assert.Equal([0.25, 0.5, 0.75], GaugeBar.Resolve(s, "바드", "진실된용맹", Sections).Splits);
    }

    [Theory]
    [InlineData(new[] { 0.25, 0.75 }, new[] { "R", "G", "P" })]                    // 50% removed: the merged segment keeps the colour it started with
    [InlineData(new[] { 0.25, 0.5, 0.6, 0.75 }, new[] { "R", "G", "B", "B", "P" })]  // 60% added: both halves keep blue
    [InlineData(new[] { 0.5 }, new[] { "R", "B" })]
    public void Colours_stay_with_their_segments_when_splits_change(double[] splits, string[] colours)
    {
        var s = new Settings();
        var b = GaugeBar.Edit(s, "바드", "절실한구원");
        (b.Splits, b.UnifiedColor, b.SegmentColors) = ([0.25, 0.5, 0.75], false, ["R", "G", "B", "P"]);
        GaugeBar.SetSplits(s, "바드", "절실한구원", Sections, splits);
        Assert.Equal(splits, b.Splits);
        Assert.Equal(colours, b.SegmentColors);
    }

    [Fact]
    public void Without_a_class_default_the_bar_is_plain_and_never_glows()
    {
        var bar = GaugeBar.Resolve(new Settings(), "워로드", "고독한기사", null);
        Assert.Empty(bar.Splits);
        Assert.Equal([false], bar.Glowing(1, slotReady: true));
    }

    [Theory]
    [InlineData(0.6, new[] { true, true, false, false })]
    [InlineData(0.75, new[] { true, true, true, false })]
    [InlineData(1.0, new[] { true, true, true, true })]
    [InlineData(0.2, new[] { false, false, false, false })]
    public void Sections_glow_once_full(double value, bool[] glowing) =>
        Assert.Equal(glowing, GaugeBar.Resolve(new Settings(), "바드", "절실한구원", Sections).Glowing(value, slotReady: false));

    [Theory]
    [InlineData(0.6, true, new[] { true, true })]     // the filled part glows
    [InlineData(0.4, true, new[] { false, false })]   // below minGauge
    [InlineData(0.6, false, new[] { false, false })]  // slot not ready
    [InlineData(0.5, true, new[] { true, false })]    // the second segment has no fill yet
    public void SlotReady_glows_the_filled_part_while_the_slot_is_ready_and_the_gauge_is_high_enough(double value, bool ready, bool[] glowing) =>
        Assert.Equal(glowing, GaugeBar.Resolve(new Settings(), "워로드", "전투태세", SlotReady).Glowing(value, ready));

    [Theory]
    [InlineData(0.6, 0.0, 0.5)]    // 바드: the two full sections glow as one, the part-filled third doesn't
    [InlineData(1.0, 0.0, 1.0)]
    [InlineData(0.2, 0.0, 0.0)]    // nothing full: no span
    public void Sections_glow_as_one_span_over_the_full_sections(double value, double from, double to) =>
        Assert.Equal(to > from ? (from, to) : null,
            GaugeBar.Resolve(new Settings(), "바드", "절실한구원", Sections).GlowSpan(value, slotReady: false));

    [Theory]
    [InlineData(0.6, true, 0.0, 0.6)]    // 전태: one span over the whole fill, across the split
    [InlineData(0.6, false, 0.0, 0.0)]
    public void SlotReady_glows_as_one_span_over_the_fill(double value, bool ready, double from, double to) =>
        Assert.Equal(to > from ? (from, to) : null,
            GaugeBar.Resolve(new Settings(), "워로드", "전투태세", SlotReady).GlowSpan(value, ready));

    [Fact]
    public void Glow_turned_off_never_glows()
    {
        var s = new Settings();
        GaugeBar.Edit(s, "바드", "절실한구원").GlowEnabled = false;
        Assert.All(GaugeBar.Resolve(s, "바드", "절실한구원", Sections).Glowing(1, slotReady: true), Assert.False);
    }

    [Fact]
    public void Every_glowing_segment_shares_one_phase()
    {
        // One clock for the whole bar: the level depends only on the time and the period, so segments pulse together.
        Assert.Equal(GaugeBar.GlowLevel(1234, 1.2), GaugeBar.GlowLevel(1234, 1.2));
        Assert.Equal(0, GaugeBar.GlowLevel(0, 1.2), 3);
        Assert.Equal(1, GaugeBar.GlowLevel(600, 1.2), 3);   // half a period: brightest
    }

    static readonly GaugeRule Support = new([0.25, 0.5, 0.75], new GlowRule(GlowWhen.Sections), [1, 2, 3]);
    static readonly GaugeRule Dealer = new([0.3333, 0.6667], new GlowRule(GlowWhen.Sections), [2, 3]);

    /// <summary>색 단계: the whole fill takes the colour for how many sections are full (1 weak, 2 medium, 3 strong); a
    /// count past the rule's list keeps its last level; nothing full, the segment colours.</summary>
    [Theory]
    [InlineData(0.2, -1)]
    [InlineData(0.3, 0)]    // 1 full: weak
    [InlineData(0.6, 1)]
    [InlineData(0.8, 2)]
    [InlineData(1.0, 2)]    // the 4th section makes no difference
    public void 절실한구원_ramps_weak_medium_strong_by_full_sections(double value, int level)
    {
        var bar = GaugeBar.Resolve(new Settings(), "바드", "절실한구원", Support);
        IReadOnlyList<string> expected = level < 0 ? bar.SegmentColors : [.. Enumerable.Repeat(bar.RampColors[level], 4)];
        Assert.Equal(expected, bar.ColorsAt(value));
    }

    [Theory]
    [InlineData(0.3, -1)]
    [InlineData(0.4, 1)]    // 1 full: medium
    [InlineData(0.7, 2)]
    [InlineData(1.0, 2)]
    public void 진실된용맹_ramps_medium_then_strong(double value, int level)
    {
        var bar = GaugeBar.Resolve(new Settings(), "바드", "진실된용맹", Dealer);
        IReadOnlyList<string> expected = level < 0 ? bar.SegmentColors : [.. Enumerable.Repeat(bar.RampColors[level], 3)];
        Assert.Equal(expected, bar.ColorsAt(value));
    }

    [Fact]
    public void The_ramp_defaults_to_the_fill_hue_deepening_and_the_user_may_change_it_for_the_class_or_turn_it_off()
    {
        var s = new Settings();
        var colours = GaugeBar.Resolve(s, "바드", "절실한구원", Support).RampColors;
        static int Sum(string hex) => Convert.ToInt32(hex[1..3], 16) + Convert.ToInt32(hex[3..5], 16) + Convert.ToInt32(hex[5..7], 16);
        Assert.Equal(3, colours.Count);
        Assert.True(Sum(s.Gauge.Fill) > Sum(colours[0]) && Sum(colours[0]) > Sum(colours[1]) && Sum(colours[1]) > Sum(colours[2]));

        var b = GaugeBar.Edit(s, "바드", "절실한구원");
        b.RampColors = ["#111111", "#222222", "#333333"];
        Assert.Equal(["#333333", "#333333", "#333333", "#333333"], GaugeBar.Resolve(s, "바드", "절실한구원", Support).ColorsAt(1));
        Assert.Equal(colours, GaugeBar.Resolve(s, "바드", "진실된용맹", Dealer).RampColors);   // this class only

        b.Ramp = false;
        var off = GaugeBar.Resolve(s, "바드", "절실한구원", Support);
        Assert.Equal(off.SegmentColors, off.ColorsAt(1));
        var none = GaugeBar.Resolve(new Settings(), "바드", "절실한구원", Sections);   // no ramp in the class data
        Assert.Equal(none.SegmentColors, none.ColorsAt(1));
    }

    const string Blue = "#0A84E0", Gold = "#D7B932";
    static readonly GaugeRule Crystals = Support with { Colors = new(Blue, Gold) };

    /// <summary>바드 by default: the section still filling is gold, full ones blue (deepening by 색 단계 from that blue).</summary>
    [Fact]
    public void A_section_still_filling_takes_the_filling_colour_and_full_ones_the_class_colour()
    {
        var bar = GaugeBar.Resolve(new Settings(), "바드", "절실한구원", Crystals);
        Assert.Equal(GaugeBar.RampDefaults(Blue), bar.RampColors);
        Assert.Equal(Gold, bar.ColorsAt(0.1)[0]);
        var one = bar.ColorsAt(0.3);
        Assert.Equal((bar.RampColors[0], Gold), (one[0], one[1]));
        Assert.All(bar.ColorsAt(1), c => Assert.Equal(bar.RampColors[2], c));

        var s = new Settings();
        GaugeBar.Edit(s, "바드", "절실한구원").Ramp = false;
        var plain = GaugeBar.Resolve(s, "바드", "절실한구원", Crystals).ColorsAt(0.3);
        Assert.Equal((Blue, Gold), (plain[0], plain[1]));
    }

    [Fact]
    public void The_user_may_change_the_full_and_filling_colours_for_the_class()
    {
        var s = new Settings();
        var b = GaugeBar.Edit(s, "바드", "절실한구원");
        (b.Ramp, b.FullColor, b.FillingColor) = (false, "#112233", "#445566");
        var bar = GaugeBar.Resolve(s, "바드", "절실한구원", Crystals);
        var c = bar.ColorsAt(0.3);
        Assert.Equal(("#112233", "#445566"), (c[0], c[1]));
        Assert.Equal("#112233", bar.Fill);
        Assert.Equal(Gold, GaugeBar.Resolve(s, "바드", "진실된용맹", Dealer with { Colors = new(Blue, Gold) }).ColorsAt(0.1)[0]);   // this class only
    }

    [Fact]
    public void Without_class_colours_the_bar_uses_the_gauge_fill_colour()
    {
        var s = new Settings();
        var bar = GaugeBar.Resolve(s, "바드", "절실한구원", Support);
        Assert.Equal(s.Gauge.Fill, bar.Fill);
        Assert.Null(bar.FillingColor);
        Assert.Equal(s.Gauge.Fill, bar.ColorsAt(0.1)[0]);
    }
}
