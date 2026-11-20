using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Where every box, bar and label of a player's plate goes (BuildLayout, LayoutPlayer), and hit-testing them (PartAt, PartRect).</summary>
public sealed partial class OverlayRenderer
{
    sealed class PlayerLayout(Player player, string key, double unit)
    {
        public Player Player = player;
        public string Key = key;
        public double Unit = unit;
        public Rect Plate;                  // around everything drawn
        public List<(Rect Plate, int[] Keys)>? Split;   // yours, skills placed apart: a plate per group of boxes
        public Rect? Gauge;
        public Rect? GaugeHome;             // 따로: where the gauge would be drawn on this plate (the gauge window's first spot)
        public GaugeBar? Bar;               // yours: splits, segment colours and glow (peers: the plain bar)
        public Appearance? Look;            // yours: the chosen cooldown colours (peers: dimmed automatically)
        public CooldownView Cooldown = new();   // 쿨 진행 표시 and 남은 쿨 (초): the class's (peers: your 나 of their class)
        public double GridTop;              // top of the boxes (below the gauge)
        public Point Anchor;                // kept still on screen when the layout changes (OverlayWindow.Refresh)
        public Point CellOrigin;            // where cell (0, 0) is drawn
        public (double X, double Y) Scale;  // DIP per cell unit
        public bool Free;                   // laid out from cells (FreeLayout), not the fixed grid
        public Rect? Docked;                // the fixed grid: what the groups dock in (DockLayout), for group drags
        public readonly Dictionary<DockGroup, Rect> Groups = [];
        public bool GaugeUpright, BuffsUpright;   // 방향 as docked (DockLayout): standing, filled from the bottom
        public double BarFull;              // the fixed grid's buff bar at 길이 1, before the 2 px floor and rounding
        public readonly Dictionary<BuffKind, double> BarFulls = [];   // per bar where rows differ (빈 칸 줄이기); else BarFull
        public bool Peer;
        public string? Name, Number;
        public Point NameAt, NumberAt;
        public Rect? Icon;
        public readonly List<(int Key, Rect Rect, Color Color)> Boxes = [];
    }

    protected override Size MeasureOverride(Size available)
    {
        _clips.Clear();
        if (DebuffsOnly) return DebuffPanelSize();
        if (JudgmentOnly) return JudgmentPanelSize();
        if (AmsuOnly) return AmsuPanelSize();
        if (RaidOnly) return RaidPanelSize();
        if (MeterOnly) return MeterPanelSize();
        if (RingOnly) return RingPanelSize();
        _layout = BuildLayout();
        return _layout is { } l ? new Size(l.Plate.Right + Bleed, l.Plate.Bottom + Bleed) : new Size(0, 0);
    }

    PlayerLayout? BuildLayout()
    {
        if (_player is not { } p) return null;
        if (Peer < 0 && BuffsOnly)
        {
            // 버프 막대 따로 두기: nothing but the bars, at this window's own size; its own cells under 자유 배치, else the stack.
            var bars = ClassProfiles.Mine(Settings) with { Groups = Enum.GetValues<SlotGroup>().ToDictionary(g => g, _ => false), Gauge = false, Layout = ClassProfiles.BuffCells(Settings) };
            return LayoutPlayer(p, "buffs", BoxSize(Settings.Buffs.Width), BoxSize(Settings.Buffs.Height), 0, Bleed, bars, leadH: 0);
        }
        if (Peer < 0 && GaugeOnly) return LayoutGauge(p);
        if (Peer < 0)
        {
            var mine = Target is { } t ? ClassProfiles.Mine(Settings, t.Key) : ClassProfiles.Mine(Settings);
            if (!Settings.Overlay.SkillRead && !ShowHidden)   // 스킬창 읽기 off: unread boxes would stay as last read; the gauge is its own part
                mine = mine with { Groups = Enum.GetValues<SlotGroup>().ToDictionary(g => g, _ => false) };
            var own = LayoutPlayer(p, "", BoxSize(mine.Width), BoxSize(mine.Height), 0, Bleed, mine, leadH: 0);
            if (own is not null) (own.Look, own.Cooldown) = (mine, mine.Cooldown);
            if (own is not null) own.Bar = ResolveBar(p);
            return own;
        }

        // What of the member to show, where, in what colours and at what size follows their class (ClassProfiles).
        var party = Settings.Party;
        var cls = Emblems.Class(p.Snapshot.ClassId);
        var look = ClassProfiles.Member(Settings, cls?.Name, cls?.Role ?? Role.딜러, p.Look, cls);
        double bw = BoxSize(look.Width, PeerShare, ClassProfiles.MinFitScale), bh = BoxSize(look.Height, PeerShare, ClassProfiles.MinFitScale);
        double font = NameFont(bh);
        // Left of the boxes, level with them (below any gauge): the slot number (파티원 순서), then the class emblem
        // (Emblems) with the name centred under it, or either alone. With both, the emblem gives up the name's line of
        // the grid height (never shrinking below one line) before 아이콘 크기 scales it, and the plate grows if the two
        // still don't fit. A name alone is centred vertically.
        bool withIcon = party.Label != PeerLabel.Name, withName = party.Label != PeerLabel.Icon;
        double colH = 2 * bh + Gap;
        string name = PlayerName.Clip(p.Name, party.NameLength);
        string slotLabel = PartySlots.Label(Peer, party.MyNumber);
        var number = NameText(slotLabel, font);
        double numberW = Math.Ceiling(number.Width);
        double nameW = withName ? Math.Ceiling(NameText(name, font).Width) : 0;
        double nameH = withName ? Math.Ceiling(NameText("가", font).Height) : 0;
        double iconScale = Math.Clamp(party.IconScale, MinIconScale, MaxIconScale);
        double iconW = !withIcon ? 0 : Math.Round((withName ? Math.Max(nameH, colH - nameH - Gap) : colH) * iconScale);
        double leadW = Math.Max(iconW, nameW);
        double leadH = withIcon ? iconW + (withName ? Gap + nameH : 0) : 0;
        var l = LayoutPlayer(p, "peer:" + p.Name, bw, bh, numberW + Gap + leadW + Gap * 2, Bleed, look, leadH);
        if (l is null) return null;
        (l.Peer, l.Cooldown) = (true, look.Cooldown);
        double left = l.Plate.X + Pad;
        l.Number = slotLabel;
        l.NumberAt = new Point(left, l.GridTop + (colH - number.Height) / 2);
        left += numberW + Gap;
        if (withIcon) l.Icon = new Rect(left + (leadW - iconW) / 2, l.GridTop, iconW, iconW);
        if (withName)
        {
            var text = NameText(name, font);
            l.Name = name;
            l.NameAt = withIcon
                ? new Point(left + (leadW - text.Width) / 2, l.GridTop + iconW + Gap)
                : new Point(left, l.GridTop + (colH - text.Height) / 2);
        }
        return l;
    }

    /// <param name="a">What is shown, where and in what colours (<see cref="ClassProfiles"/>), decided once per layout.</param>
    /// <param name="leadH">Height of what stands left of the boxes, from the grid top; the plate grows to fit it.</param>
    PlayerLayout? LayoutPlayer(Player p, string key, double bw, double bh, double lead, double top, Appearance a, double leadH)
    {
        var s = p.Snapshot;
        bool Show(SlotGroup g) => s.Groups.Contains(g) && a.Groups.GetValueOrDefault(g, true);
        var colors = Enum.GetValues<SlotGroup>().ToDictionary(g => g, g => Parse(a.Colors[g]));
        Color Col(SlotGroup g) => colors[g];
        Color SkillCol(SlotId id) => a.SlotColors?.GetValueOrDefault(id) is { } own ? Parse(own) : colors[SlotGroup.일반스킬];   // a 일반스킬's own colour
        // The gauge's thickness follows the box height, as it did the one size.
        double homeH = a.Gauge && s.Gauge is not null ? SplitLayout.GaugeHeight(Settings.Gauge.Thickness, bh) : 0;
        double gaugeH = GaugeApart ? 0 : homeH;   // 따로: the gauge has a window of its own, the plate keeps no strip for it
        if (a.Layout is { } cells) return LayoutCells(p, key, cells, a.Hidden, bw, bh, lead, top, Show, Col, SkillCol, gaugeH, leadH, homeH);
        var hidden = a.Hidden;

        var l = new PlayerLayout(p, key, bh);
        var buffs = ShownBuffs(s, BarArrangement.Of(a.BarOrder));
        // Each bar's own 두께 and 길이 (BuffIndicator), clamped as the settings window offers them.
        double Thick(BuffKind k) => BarSizes.Thickness(Settings.Buffs.Of(k));
        double shared = BarSizes.Shared(Settings.Buffs);   // 칸 맞춤: one 길이 for every bar
        double Long(BuffKind k) => shared;
        double SideW(BuffKind k) => Math.Max(2, Math.Round(SideBuffWidth * bw / Box * Thick(k)));
        double barH = FreeLayout.BuffHeight * bh / Box;
        // The plate's content (groups docked around the grid, DockLayout) starts after the peer's lead.
        double ox = Bleed + Pad + lead, oy = top + Pad;
        l.CellOrigin = new Point(ox, oy);
        l.Scale = (bw / Box, bh / Box);
        double colH = 2 * bh + Gap;

        // 칸 맞춤 (SlotGrid): 각성 | 초각 | QWER / ASDF | 아덴, closing up what 표시 turned off with 빈 칸 줄이기.
        bool Present(SlotId id) => id switch
        {
            SlotId.각성기 => Show(SlotGroup.각성기),
            SlotId.초각스 => Show(SlotGroup.초각스),
            SlotId.아덴1 or SlotId.아덴2 => Show(SlotGroup.아덴스킬) && s.AdenSlots.Contains(id),
            _ => Show(SlotGroup.일반스킬),
        };
        Color SlotColor(SlotId id) => id switch
        {
            SlotId.각성기 => Col(SlotGroup.각성기),
            SlotId.초각스 => Col(SlotGroup.초각스),
            SlotId.아덴1 or SlotId.아덴2 => Col(SlotGroup.아덴스킬),
            _ => SkillCol(id),
        };
        var placed = SlotGrid.Arrange(SlotArrangement.Of(a.SlotOrder), Present, hidden, a.CloseGaps, bw, bh, Gap);
        bool anyColumn = placed.Width > 0;
        if (!anyColumn && gaugeH == 0 && buffs.Count == 0) return null;

        // 합쳐서: the dock order and 방향 (slot-layout.plan.md §3); the buffs' own window: its bars alone, 세로 or 가로.
        IReadOnlyList<DockSpot> order = BuffsOnly ? [new DockSpot(DockGroup.Buffs, Settings.Buffs.Side ? DockSide.Left : DockSide.Bottom)] : DockOrder.Of(a.DockOrder);
        bool Here(DockGroup g) => g == DockGroup.Gauge ? gaugeH > 0 : buffs.Count > 0;
        double flatBuffsH = BuffsOnly
            ? BuffStack.Arrange(buffs, false, 1, 1, Gap, k => barH * Thick(k)).Height
            : BarRows.Arrange(BarArrangement.Of(a.BarOrder), buffs.Contains, a.CloseGaps, 100, Gap, k => barH * Thick(k), Long).Select(b => b.Cell.Bottom).DefaultIfEmpty(0).Max();
        double Depth(DockGroup g, bool upright, double length) => g == DockGroup.Gauge ? gaugeH
            : upright ? BuffStack.Arrange(buffs, true, length, shared, Gap, SideW).Width : flatBuffsH;
        // Without boxes, flat groups still get a grid's width; upright ones alone take none.
        double skillW = anyColumn ? placed.Width : order.Any(o => Here(o.Group) && !o.Upright) ? 4 * bw : 0;
        var d = DockLayout.Arrange(order, skillW, anyColumn ? Math.Max(colH, leadH) : 0, Here, Depth, colH, Gap);
        double x0 = ox + d.Skills.X, gridTop = oy + d.Skills.Y;
        Rect At(Cell c) => new(ox + c.X, oy + c.Y, c.W, c.H);
        l.GridTop = gridTop;
        l.Anchor = new Point(x0, gridTop);

        foreach (var (id, c, off) in placed.Boxes)
        {
            var r = new Rect(x0 + c.X, gridTop + c.Y, c.W, c.H);
            if (id == SlotId.각성기)   // 변화 on top, drawn even while 각성기 keeps an empty spot
            {
                double ch = Math.Round(bh * ChangedShare);
                l.Boxes.Add((ChangedKey, new Rect(r.X, r.Y, r.Width, ch), Parse(Settings.Overlay.AwakeningChangedColor)));
                r = new Rect(r.X, r.Y + ch + Gap, r.Width, Math.Max(0, r.Height - ch - Gap));
            }
            if (!off) l.Boxes.Add(((int)id, r, SlotColor(id)));
        }
        if (d.Groups.TryGetValue(DockGroup.Gauge, out var gauge)) (l.Gauge, l.GaugeUpright) = (At(gauge.Cell), gauge.Upright);
        if (d.Groups.TryGetValue(DockGroup.Buffs, out var bars)) LayoutBuffs(l, At(bars.Cell), bars.Upright, buffs, a, barH, shared, Thick, SideW);
        l.Plate = new Rect(Bleed, top, lead + d.Width + 2 * Pad, d.Height + 2 * Pad);
        l.Docked = new Rect(ox, oy, d.Width, d.Height);
        if (anyColumn) l.Groups[DockGroup.Skills] = At(d.Skills);
        foreach (var (g, c) in d.Groups) l.Groups[g] = At(c.Cell);
        if (GaugeApart && homeH > 0) l.GaugeHome = new Rect(x0, gridTop - homeH - Gap, d.Skills.W, homeH);
        return l;
    }

    /// <summary>The buff bars in their docked cell <paramref name="r"/>. Upright: side by side, the first bar nearest the grid,
    /// a shorter bar standing on the bottom edge (BuffStack). Flat: rows under one another (BarRows: 공증 낙인 / 아덴 초각 / 뎀증,
    /// sharing a row's width with 빈 칸 줄이기, else two to a row, each half wide); in the buffs' own window one to a row.</summary>
    void LayoutBuffs(PlayerLayout l, Rect r, bool upright, IReadOnlyList<BuffKind> buffs, Appearance a, double barH, double shared,
        Func<BuffKind, double> thick, Func<BuffKind, double> sideW)
    {
        l.BuffsUpright = upright;
        Color BuffColor(BuffKind k) => Parse(Settings.Buffs.Of(k).Color);
        void Add(BuffKind k, Rect at) => l.Boxes.Add(((int)(FreeLayout.FirstBuff + (int)k), at, BuffColor(k)));
        if (upright)
        {
            l.BarFull = r.Height;
            // BuffStack puts the first bar rightmost: nearest the grid when docked left; mirrored on the right.
            bool mirror = r.X > l.Anchor.X;
            foreach (var (k, c) in BuffStack.Arrange(buffs, true, r.Height, shared, Gap, sideW).Bars)
                Add(k, new Rect(mirror ? r.Right - c.X - c.W : r.X + c.X, r.Y + c.Y, c.W, c.H));
            return;
        }
        if (BuffsOnly)
        {
            l.BarFull = r.Width;
            foreach (var (k, c) in BuffStack.Arrange(buffs, false, r.Width, shared, Gap, k => barH * thick(k)).Bars)
                Add(k, new Rect(r.X, r.Y + c.Y, c.W, c.H));
            return;
        }
        l.BarFull = (r.Width - Gap) / 2;
        foreach (var (k, c, spot) in BarRows.Arrange(BarArrangement.Of(a.BarOrder), buffs.Contains, a.CloseGaps, r.Width, Gap, k => barH * thick(k), _ => shared))
        {
            Add(k, new Rect(r.X + c.X, r.Y + c.Y, c.W, c.H));
            l.BarFulls[k] = spot;
        }
    }

    /// <summary>The part drawn at <paramref name="at"/> (this element's coordinates) and its rectangle, or null.</summary>
    public (Part Part, Rect Rect)? PartAt(Point at)
    {
        if (_layout is not { } l || GaugeOnly) return null;   // the gauge window is picked whole
        for (int i = l.Boxes.Count - 1; i >= 0; i--)
            if (l.Boxes[i].Rect.Contains(at)) return ((Part)l.Boxes[i].Key, l.Boxes[i].Rect);
        return l.Gauge is { } g && g.Contains(at) ? (Part.게이지, g) : null;
    }

    /// <summary>Where <paramref name="part"/> is drawn (this element's coordinates), or null when it isn't.</summary>
    public Rect? PartRect(Part part)
    {
        if (_layout is not { } l) return null;
        if (part == Part.게이지) return l.Gauge;
        foreach (var b in l.Boxes) if ((Part)b.Key == part) return b.Rect;
        return null;
    }

    /// <summary>What is on screen as a free layout: each drawn part's cell, and the fixed-grid spot of the rest. Turning
    /// the fixed grid into a free layout with this leaves every shown box where it was.</summary>
    public Dictionary<Part, Cell> CellsAsDrawn()
    {
        var cells = FreeLayout.Default(Settings.Gauge.Thickness);
        if (_layout is not { } l) return cells;
        var (sx, sy) = l.Scale;
        Cell ToCell(Rect r) => new((r.X - l.CellOrigin.X) / sx, (r.Y - l.CellOrigin.Y) / sy, r.Width / sx, r.Height / sy);
        foreach (var (k, r, _) in l.Boxes)
            cells[(Part)k] = (Part)k >= FreeLayout.FirstBuff && Settings.Buffs.Of((BuffKind)(k - (int)FreeLayout.FirstBuff)) is var bar
                ? BarSizes.Unsized(ToCell(r), BarSizes.Length(bar), BarSizes.Thickness(bar), l.BuffsUpright)   // drawn at 길이 × 두께
                : ToCell(r);
        if (l.Gauge is { } g) cells[Part.게이지] = ToCell(g);
        return cells;
    }
}
