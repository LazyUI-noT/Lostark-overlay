using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class DockTests
{
    const double SkillW = 100, SkillH = 50, Gap = 2, GaugeT = 6, FlatT = 10, StandT = 8, MinTall = 50;

    static double Depth(DockGroup g, bool upright, double length) => g == DockGroup.Gauge ? GaugeT : upright ? StandT : FlatT;

    static Docked Arrange(string order, double w = SkillW, double h = SkillH, bool gauge = true, bool buffs = true) =>
        DockLayout.Arrange(DockOrder.Of(order), w, h, g => g == DockGroup.Gauge ? gauge : buffs, Depth, MinTall, Gap);

    [Fact]
    public void GaugeSetUpright_DockedTopOrBottom_LiesFlatAcrossTheWidth()
    {
        // dock-gbv-bth.png (orchestrator, 2026-11-20): a 세로 gauge over or under the skills follows the side, flat.
        var d = Arrange("gtv,bba");
        Assert.False(d.Groups[DockGroup.Gauge].Upright);
        Assert.Equal(new Cell(0, 0, 100, 6), d.Groups[DockGroup.Gauge].Cell);
        Assert.False(new DockSpot(DockGroup.Gauge, DockSide.Bottom, Direction.Upright).Upright);
        Assert.True(new DockSpot(DockGroup.Buffs, DockSide.Bottom, Direction.Upright).Upright);   // 버프 세로 under the skills stays
    }

    [Fact]
    public void Default_GaugeTopBuffsBottom_AsToday()
    {
        var d = Arrange(DockOrder.DefaultText);
        Assert.Equal(new Cell(0, 0, 100, 6), d.Groups[DockGroup.Gauge].Cell);
        Assert.Equal(new Cell(0, 8, 100, 50), d.Skills);
        Assert.Equal(new Cell(0, 60, 100, 10), d.Groups[DockGroup.Buffs].Cell);
        Assert.False(d.Groups[DockGroup.Buffs].Upright);
        Assert.Equal((100.0, 70.0), (d.Width, d.Height));
    }

    [Fact]
    public void GaugeLeftAfterBuffsBottom_StandsAsTallAsSkillsAndBuffs()
    {
        var d = Arrange("bba,gla");
        Assert.Equal(new Cell(0, 0, 6, 62), d.Groups[DockGroup.Gauge].Cell);
        Assert.True(d.Groups[DockGroup.Gauge].Upright);
        Assert.Equal(new Cell(8, 0, 100, 50), d.Skills);
        Assert.Equal(new Cell(8, 52, 100, 10), d.Groups[DockGroup.Buffs].Cell);
        Assert.Equal((108.0, 62.0), (d.Width, d.Height));
    }

    [Fact]
    public void GaugeLeftBuffsRight_BothStandBesideSkills()
    {
        var d = Arrange("gla,bra");
        Assert.Equal(new Cell(0, 0, 6, 50), d.Groups[DockGroup.Gauge].Cell);
        Assert.Equal(new Cell(8, 0, 100, 50), d.Skills);
        Assert.Equal(new Cell(110, 0, 8, 50), d.Groups[DockGroup.Buffs].Cell);
        Assert.True(d.Groups[DockGroup.Buffs].Upright);
    }

    [Fact]
    public void BuffsTopGaugeBottom()
    {
        var d = Arrange("bta,gba");
        Assert.Equal(new Cell(0, 0, 100, 10), d.Groups[DockGroup.Buffs].Cell);
        Assert.Equal(new Cell(0, 12, 100, 50), d.Skills);
        Assert.Equal(new Cell(0, 64, 100, 6), d.Groups[DockGroup.Gauge].Cell);
    }

    [Fact]
    public void UprightUnderSkills_IsAsTallAsSkills()
    {
        var d = Arrange("gta,bbv");
        Assert.Equal(new Cell(0, 60, 8, 50), d.Groups[DockGroup.Buffs].Cell);
        Assert.True(d.Groups[DockGroup.Buffs].Upright);
    }

    [Fact]
    public void FlatBesideSkills_IsAsWideAsSkills()
    {
        var d = Arrange("gta,blh");
        Assert.Equal(new Cell(0, 0, 100, 10), d.Groups[DockGroup.Buffs].Cell);   // level with the top of what is placed
        Assert.Equal(new Cell(102, 8, 100, 50), d.Skills);
        Assert.False(d.Groups[DockGroup.Buffs].Upright);
    }

    [Fact]
    public void AbsentGroup_TakesNoSpace()
    {
        var d = Arrange(DockOrder.DefaultText, gauge: false);
        Assert.False(d.Groups.ContainsKey(DockGroup.Gauge));
        Assert.Equal(new Cell(0, 0, 100, 50), d.Skills);
        Assert.Equal(new Cell(0, 52, 100, 10), d.Groups[DockGroup.Buffs].Cell);
    }

    [Fact]
    public void NoSkills_NoGapAndUprightAtLeastMinTall()
    {
        var d = Arrange("gta,bla", w: 0, h: 0, gauge: false);
        Assert.Equal(new Cell(0, 0, 8, 50), d.Groups[DockGroup.Buffs].Cell);
        Assert.Equal((8.0, 50.0), (d.Width, d.Height));
    }

    [Fact]
    public void Order_RoundTrips_AndBadTextIsDefault()
    {
        Assert.Equal("bba,glv", DockOrder.Format(DockOrder.Of("bba,glv")));
        Assert.Equal(DockOrder.DefaultText, DockOrder.Format(DockOrder.Default));
        foreach (var bad in new[] { null, "", "gta", "gta,gba", "xta,bba", "gxa,bba", "gtx,bba", "gta,bba,", "gtaa,bba" })
            Assert.Null(DockOrder.Parse(bad));
        Assert.Equal(DockOrder.Default, DockOrder.Of("nonsense"));
    }

    [Fact]
    public void SwapTwoGroups_TradesSides()
    {
        var after = DockDrop.Swap(DockOrder.Default, DockGroup.Gauge, DockGroup.Buffs);
        Assert.Equal("gba,bta", DockOrder.Format(after));
    }

    [Fact]
    public void SwapTwoGroupsOnOneSide_TradesPlaces()
    {
        var after = DockDrop.Swap(DockOrder.Of("gta,bta"), DockGroup.Buffs, DockGroup.Gauge);
        Assert.Equal("bta,gta", DockOrder.Format(after));
    }

    [Fact]
    public void SwapWithSkills_PassesToTheOppositeSideNextToSkills()
    {
        var after = DockDrop.Swap(DockOrder.Default, DockGroup.Buffs, DockGroup.Skills);
        Assert.Equal("bta,gta", DockOrder.Format(after));
        var d = DockLayout.Arrange(after, SkillW, SkillH, _ => true, Depth, MinTall, Gap);
        Assert.Equal(new Cell(0, 0, 100, 6), d.Groups[DockGroup.Gauge].Cell);
        Assert.Equal(new Cell(0, 8, 100, 10), d.Groups[DockGroup.Buffs].Cell);
        Assert.Equal(new Cell(0, 20, 100, 50), d.Skills);
        Assert.Equal(DockOrder.Format(after), DockOrder.Format(DockDrop.Swap(DockOrder.Default, DockGroup.Skills, DockGroup.Buffs)));
    }

    [Fact]
    public void DockAtEdge_DocksLastOnThatSide_KeepingDirection()
    {
        var after = DockDrop.DockAt(DockOrder.Of("gtv,bba"), DockGroup.Gauge, DockSide.Left);
        Assert.Equal("bba,glv", DockOrder.Format(after));
    }

    [Fact]
    public void Depth_IsMeasuredFromTheEdgeFacingTheDragged()
    {
        var left = new Cell(0, 0, 20, 10);
        var right = new Cell(50, 0, 20, 10);
        Assert.Equal(0.5, DockDrop.Depth(left, right, 60, 5), 3);     // other to the right: from its left edge
        Assert.Equal(0.25, DockDrop.Depth(right, left, 15, 5), 3);    // other to the left: from its right edge
        var top = new Cell(0, 0, 20, 10);
        var below = new Cell(0, 40, 20, 10);
        Assert.Equal(0.25, DockDrop.Depth(below, top, 5, 7.5), 3);    // other above: from its bottom edge
        Assert.Equal(0.0, DockDrop.Depth(left, right, 30, 5), 3);     // outside it
    }

    static readonly Dictionary<DockGroup, Cell> Plate = new()
    {
        [DockGroup.Gauge] = new(0, 0, 100, 6),
        [DockGroup.Skills] = new(0, 8, 100, 50),
        [DockGroup.Buffs] = new(0, 60, 100, 10),
    };
    static readonly Cell Area = new(0, 0, 100, 70);

    [Fact]
    public void Aim_ShallowInAnotherGroup_StaysABlockDrag()
    {
        var aim = DockDrop.AimAt(DockGroup.Skills, false, Plate, Area, 50, 61, 10);   // 1 of 10 into 버프 from its top
        Assert.Equal(new DockAim(false, null, null), aim);
    }

    [Fact]
    public void Aim_PastASixth_BecomesAGroupDrag_AndPastHalf_Swaps()
    {
        Assert.Equal(new DockAim(true, null, null), DockDrop.AimAt(DockGroup.Skills, false, Plate, Area, 50, 63, 10));
        Assert.Equal(new DockAim(true, DockGroup.Buffs, null), DockDrop.AimAt(DockGroup.Skills, false, Plate, Area, 50, 66, 10));
    }

    [Fact]
    public void Aim_AGroupDragJustOutsideThePlate_DocksThere()
    {
        Assert.Equal(new DockAim(true, null, DockSide.Left), DockDrop.AimAt(DockGroup.Gauge, true, Plate, Area, -4, 30, 10));
        Assert.Equal(new DockAim(false, null, null), DockDrop.AimAt(DockGroup.Buffs, false, Plate, Area, -4, 30, 10));   // a bar drag: no dock
    }

    [Fact]
    public void Apply_SwapsOrDocks_ElseKeepsTheOrder()
    {
        Assert.Equal("gba,bta", DockOrder.Format(DockDrop.Apply(DockOrder.Default, DockGroup.Gauge, new(true, DockGroup.Buffs, null))));
        Assert.Equal("bba,gla", DockOrder.Format(DockDrop.Apply(DockOrder.Default, DockGroup.Gauge, new(true, null, DockSide.Left))));
        Assert.Same(DockOrder.Default, DockDrop.Apply(DockOrder.Default, DockGroup.Gauge, new(true, null, null)));
    }

    [Fact]
    public void Edge_IsTheSideJustOutsideThePlate()
    {
        var plate = new Cell(0, 0, 100, 50);
        Assert.Equal(DockSide.Left, DockDrop.EdgeAt(plate, -5, 25, 10));
        Assert.Equal(DockSide.Bottom, DockDrop.EdgeAt(plate, 50, 55, 10));
        Assert.Null(DockDrop.EdgeAt(plate, 50, 25, 10));    // inside
        Assert.Null(DockDrop.EdgeAt(plate, 50, 80, 10));    // too far
        Assert.Null(DockDrop.EdgeAt(plate, -5, -5, 10));    // a corner: no side
    }

    [Fact]
    public void Edge_IsReachable_WithThePlateAgainstTheWindowEdge()
    {
        // The docked area sits Bleed + PlatePad (10 + 4) in from the renderer's edge, and the drag holds the mouse, so the
        // band between the window edge (x = 0) and the area is always under the cursor's reach (24 screen px at 100%).
        const double inset = 14;
        var area = new Cell(inset, inset, 100, 50);
        Assert.Equal(DockSide.Left, DockDrop.EdgeAt(area, 0, 30, 24));
        Assert.Equal(DockSide.Top, DockDrop.EdgeAt(area, 50, 0, 24));
        Assert.Equal(DockSide.Left, DockDrop.EdgeAt(area, inset - 1, 30, 24));
    }
}
