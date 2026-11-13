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
        public GaugeBar? Bar;               // yours: splits, segment colours and glow (peers: the plain bar)
        public Appearance? Look;            // yours: the chosen cooldown colours (peers: dimmed automatically)
        public CooldownView Cooldown = new();   // 쿨 진행 표시 and 남은 쿨 (초): the class's (peers: your 나 of their class)
        public double GridTop;              // top of the boxes (below the gauge)
        public Point Anchor;                // kept still on screen when the layout changes (OverlayWindow.Refresh)
        public Point CellOrigin;            // where cell (0, 0) is drawn
        public (double X, double Y) Scale;  // DIP per cell unit
        public bool Free;                   // laid out from cells (FreeLayout), not the fixed grid
        public double BarFull;              // the fixed grid's buff bar at 길이 1, before the 2 px floor and rounding
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
        _layout = BuildLayout();
        return _layout is { } l ? new Size(l.Plate.Right + Bleed, l.Plate.Bottom + Bleed) : new Size(0, 0);
    }

    PlayerLayout? BuildLayout()
    {
        if (_player is not { } p) return null;
        if (Peer < 0 && BuffsOnly)
        {
            // 버프 막대 따로 두기: the fixed grid with nothing but the bars, at this window's own size.
            var bars = ClassProfiles.Mine(Settings) with { Groups = Enum.GetValues<SlotGroup>().ToDictionary(g => g, _ => false), Gauge = false, Layout = null };
            return LayoutPlayer(p, "buffs", BoxSize(Settings.Buffs.Width), BoxSize(Settings.Buffs.Height), 0, Bleed, bars, leadH: 0);
        }
        if (Peer < 0)
        {
            var mine = Target is { } t ? ClassProfiles.Mine(Settings, t.Key) : ClassProfiles.Mine(Settings);
            if (!Settings.Overlay.SkillRead && !ShowHidden)   // 스킬창 읽기 off: unread boxes would stay as last read; the gauge is its own part
                mine = mine with { Groups = Enum.GetValues<SlotGroup>().ToDictionary(g => g, _ => false) };
            var own = LayoutPlayer(p, "", BoxSize(mine.Width), BoxSize(mine.Height), 0, Bleed, mine, leadH: 0);
            if (own is not null) (own.Look, own.Cooldown) = (mine, mine.Cooldown);
            var (barClass, barSub) = Target is { } picked ? (picked.Class, picked.Subclass)
                : Settings.ActiveCharacter is { } k && Settings.Characters.TryGetValue(k, out var c) ? (c.Class, c.Subclass) : default;
            if (own is not null && barClass is not null)
                own.Bar = GaugeBar.Resolve(Settings, barClass, barSub!, Emblems.Class(p.Snapshot.ClassId)?.Subclasses.GetValueOrDefault(barSub!));
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
        double gaugeH = a.Gauge && s.Gauge is not null ? Math.Max(2, Math.Round(Settings.Gauge.Thickness * bh / Box)) : 0;
        if (a.Layout is { } cells) return LayoutCells(p, key, cells, a.Hidden, bw, bh, lead, top, Show, Col, SkillCol, gaugeH, leadH);
        var hidden = a.Hidden;

        var l = new PlayerLayout(p, key, bh);
        // 버프 막대 위치 왼쪽 바깥: the bars take a strip left of the plate, which starts after it.
        var buffs = ShownBuffs(s);
        bool side = Settings.Buffs.Side && buffs.Count > 0;
        // Each bar's own 두께 and 길이 (BuffIndicator), clamped as the settings window offers them.
        double Thick(BuffKind k) => BarSizes.Thickness(Settings.Buffs.Of(k));
        double Long(BuffKind k) => BarSizes.Length(Settings.Buffs.Of(k));
        double SideW(BuffKind k) => Math.Max(2, Math.Round(SideBuffWidth * bw / Box * Thick(k)));
        double plateX = Bleed + (side ? buffs.Sum(k => SideW(k) + Gap) : 0);
        double x0 = plateX + Pad + lead, x = x0;
        double gridTop = top + Pad + (gaugeH > 0 ? gaugeH + Gap : 0);
        l.GridTop = gridTop;
        l.Anchor = new Point(x0, gridTop);
        l.CellOrigin = new Point(x0, top + Pad);
        l.Scale = (bw / Box, bh / Box);
        double colH = 2 * bh + Gap;
        void Add(SlotId id, Rect r, Color c) { if (!hidden.Contains(id)) l.Boxes.Add(((int)id, r, c)); }

        if (Show(SlotGroup.각성기))
        {
            double ch = Math.Round(bh * ChangedShare);
            l.Boxes.Add((ChangedKey, new Rect(x, gridTop, bw, ch), Parse(Settings.Overlay.AwakeningChangedColor)));
            Add(SlotId.각성기, new Rect(x, gridTop + ch + Gap, bw, colH - ch - Gap), Col(SlotGroup.각성기));
            x += bw + Gap;
        }
        if (Show(SlotGroup.초각스))
        {
            Add(SlotId.초각스, new Rect(x, gridTop, bw, colH), Col(SlotGroup.초각스));
            x += bw + Gap;
        }
        if (Show(SlotGroup.일반스킬))
        {
            for (int i = 0; i < 8; i++)
                Add(SlotId.일반1 + i, new Rect(x + i % 4 * (bw + Gap), gridTop + i / 4 * (bh + Gap), bw, bh), SkillCol(SlotId.일반1 + i));
            x += 4 * (bw + Gap);
        }
        var aden = new[] { SlotId.아덴1, SlotId.아덴2 }.Where(p.Snapshot.AdenSlots.Contains).ToList();
        if (Show(SlotGroup.아덴스킬) && aden.Count > 0)
        {
            double h = Math.Round((colH - (aden.Count - 1) * Gap) / aden.Count);
            for (int i = 0; i < aden.Count; i++)
                Add(aden[i], new Rect(x, gridTop + i * (h + Gap), bw, i == aden.Count - 1 ? colH - i * (h + Gap) : h), Col(SlotGroup.아덴스킬));
            x += bw + Gap;
        }

        bool anyColumn = x > x0;
        if (!anyColumn && gaugeH == 0 && buffs.Count == 0) return null;
        // Upright bars alone (the buffs' own window, 세로) take no grid width; flat ones get a grid's width.
        double right = anyColumn ? x - Gap : side && gaugeH == 0 ? plateX - Pad : x0 + 4 * bw;
        if (gaugeH > 0) l.Gauge = new Rect(x0, top + Pad, right - x0, gaugeH);
        double bottom = anyColumn ? gridTop + Math.Max(colH, leadH) : top + Pad + gaugeH;
        Color BuffColor(BuffKind k) => Parse(Settings.Buffs.Of(k).Color);
        if (side)
        {
            // Upright, as tall as the gauge and grid (at least a grid's height, when nothing else is shown), 공증 nearest
            // the plate (buff_example2: 뎀증 초각 아덴 공증).
            double tall = Math.Max(bottom - top - Pad, colH);
            bottom = top + Pad + tall;
            l.BarFull = tall;
            double bx = plateX;
            foreach (var k in buffs)   // a shorter bar stands on the bottom edge
            {
                double w = SideW(k), h = Math.Max(2, Math.Round(tall * Long(k)));
                bx -= w + Gap;
                l.Boxes.Add(((int)(FreeLayout.FirstBuff + (int)k), new Rect(bx, top + Pad + tall - h, w, h), BuffColor(k)));
            }
        }
        else
        {
            // The buff bars under the grid, two to a row, each at most half its width (from the left); in the buffs' own
            // window (가로) one to a row, top to bottom. A row is as tall as its thicker bar. Closing up like the columns.
            int perRow = BuffsOnly ? 1 : 2;
            double full = (right - x0 - (perRow - 1) * Gap) / perRow, barH = FreeLayout.BuffHeight * bh / Box, y = bottom + Gap;
            l.BarFull = full;
            for (int i = 0; i < buffs.Count; i += perRow)
            {
                var row = buffs.Skip(i).Take(perRow).ToList();
                double rowH = row.Max(k => Math.Max(2, Math.Round(barH * Thick(k))));
                for (int j = 0; j < row.Count; j++)
                    l.Boxes.Add(((int)(FreeLayout.FirstBuff + (int)row[j]), new Rect(x0 + j * (full + Gap), y,
                        Math.Max(2, Math.Round(full * Long(row[j]))), Math.Max(2, Math.Round(barH * Thick(row[j])))), BuffColor(row[j])));
                y += rowH + Gap;
            }
            bottom = y - Gap;
        }
        l.Plate = new Rect(plateX, top, right + Pad - plateX, bottom + Pad - top);
        return l;
    }

    /// <summary>The part drawn at <paramref name="at"/> (this element's coordinates) and its rectangle, or null.</summary>
    public (Part Part, Rect Rect)? PartAt(Point at)
    {
        if (_layout is not { } l) return null;
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
                ? BarSizes.Unsized(ToCell(r), BarSizes.Length(bar), BarSizes.Thickness(bar), Settings.Buffs.Side)   // drawn at 길이 × 두께
                : ToCell(r);
        if (l.Gauge is { } g) cells[Part.게이지] = ToCell(g);
        return cells;
    }
}
