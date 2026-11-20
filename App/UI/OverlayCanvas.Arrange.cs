using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>칸 맞춤 drag (slot-layout.plan.md §2): a skill block or buff bar is dragged over the others; the gold flash shows
/// where it would land, and dropping writes <c>Overlay.SlotOrder</c> / <c>BarOrder</c> as one undo step. Esc or a drop with
/// nothing flashing cancels.</summary>
public sealed partial class OverlayCanvas
{
    readonly Rectangle _drop = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Rectangle _ghost = new() { Fill = Brushes.White, Opacity = 0.35, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    SlotId? _heldSlot;
    BuffKind? _heldBar;
    bool _heldInBuffs;   // the bar is held in the 따로 window: its drops are a stack's
    Size _heldSize;
    SlotTarget? _slotTarget;
    BarTarget? _barTarget;
    DockGroup? _heldGroup;   // the plate group the held block, bar or gauge belongs to (DockLayout)
    bool _groupDrag;         // the whole group is held: past DockDrop.GroupDepth into another, or the gauge / standing bars
    DockAim? _aim;

    // 키 표시: the key a hovered box is bound to (키 설정), or a bar's name, on the canvas only.
    readonly Border _keyTip = new() { Background = new SolidColorBrush(Color.FromArgb(0xE0, 0, 0, 0)), CornerRadius = new(2), Padding = new(4, 1, 4, 1),
        Child = new TextBlock { Foreground = Brushes.White, FontSize = 12 }, IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    public bool ShowKeys { get; set; }

    void ShowKeyTip(OverlayRenderer? r, Point at)
    {
        string? text = null;
        Rect box = default;
        if (ShowKeys && r is { Peer: < 0 } && !Play.Running && r.PartAt(at) is var (part, rect))
        {
            box = rect;
            text = part <= Part.아덴2 ? _s.Keys.GetValueOrDefault((SlotId)part)
                : part >= FreeLayout.FirstBuff ? ((BuffKind)(part - FreeLayout.FirstBuff)).ToString() : null;
        }
        if (string.IsNullOrEmpty(text) || r is null) { _keyTip.Visibility = Visibility.Collapsed; return; }
        ((TextBlock)_keyTip.Child).Text = text;
        Canvas.SetLeft(_keyTip, Canvas.GetLeft(r) + box.X);
        Canvas.SetTop(_keyTip, Canvas.GetTop(r) + box.Y);
        _keyTip.Visibility = Visibility.Visible;
    }

    void BuildArrange()
    {
        _world.Children.Add(_keyTip);
        _drop.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        _world.Children.Add(_ghost);
        _world.Children.Add(_drop);
    }

    /// <summary>The block or bar at <paramref name="at"/> is picked up for an arrangement drag, when this window is drawn from
    /// the fixed grid (a free layout, a member's window and a preview have no arrangement to change); the buffs' own window
    /// is a stack of bars (<see cref="BuffStackDrop"/>).</summary>
    bool StartArrange(OverlayRenderer r, Point at)
    {
        if (_scene || r.Peer >= 0 || r.ShowsReceivedLayout || Previewing(r) || Free) return false;   // 자유 배치: boxes move alone
        if (r.PartAt(at) is not var (part, rect)) return false;
        (_heldSlot, _heldBar, _aim, _groupDrag) = (null, null, null, false);
        // On the plate a block or bar belongs to its group (DockLayout): deep enough into another, the whole group moves.
        _heldGroup = r == _mine && r.Docking is not null
            ? part <= Part.아덴2 ? DockGroup.Skills : part == Part.게이지 ? DockGroup.Gauge : part >= FreeLayout.FirstBuff ? DockGroup.Buffs : null
            : null;
        if (part <= Part.아덴2) _heldSlot = (SlotId)part;
        else if (part >= FreeLayout.FirstBuff && (r == _buffs || !r.BuffsUpright)) _heldBar = (BuffKind)(part - FreeLayout.FirstBuff);
        else if (_heldGroup is not null) _groupDrag = true;   // 아덴 게이지, or bars standing on the plate: always the whole group
        else return false;
        _heldSize = HeldSize(r, rect.Size);
        _heldInBuffs = r == _buffs;
        return true;
    }

    /// <summary>The ghost's size: the whole group's on a group drag, else the held block's or bar's.</summary>
    Size HeldSize(OverlayRenderer r, Size part) =>
        _groupDrag && _heldGroup is { } g && r.Docking?.Groups.GetValueOrDefault(g) is { } whole ? whole.Size : part;

    Size HeldPartSize(OverlayRenderer r) =>
        (_heldSlot is { } s ? r.PartRect((Part)s) : _heldBar is { } b ? r.PartRect(FreeLayout.FirstBuff + (int)b) : null)?.Size ?? _heldSize;

    void UpdateArrange(OverlayRenderer r, Point at)
    {
        (_slotTarget, _barTarget, _aim) = (null, null, null);
        Cell? anchor = null;
        DropSide side = DropSide.Middle;
        // A block or bar is its group's only while the cursor is deep enough in another (no latch): back over a slot or bar
        // it is the block again. The gauge and standing bars are always the whole group.
        bool block = _heldSlot is not null || _heldBar is not null;
        var partSize = block ? HeldPartSize(r) : _heldSize;
        _groupDrag = !block && _heldGroup is not null;
        if (_heldGroup is { } held && r.Docking is { } dock
            && DockDrop.AimAt(held, _groupDrag, dock.Groups.ToDictionary(g => g.Key, g => ToCell(g.Value)), ToCell(dock.Area), at.X, at.Y,
                Math.Max(FreeLayout.Gap, DockReach / (_v?.Scale ?? 1))) is { Group: true } aim)
        {
            (_groupDrag, _aim) = (true, aim);
            if (aim.Swap is { } with) anchor = ToCell(dock.Groups[with]);
            else if (aim.Edge is { } edge) (anchor, side) = (ToCell(dock.Area), edge switch
            {
                DockSide.Top => DropSide.Top,
                DockSide.Bottom => DropSide.Bottom,
                DockSide.Left => DropSide.Left,
                _ => DropSide.Right,
            });
        }
        else if (_groupDrag) { }   // a whole group over nothing: no flash, the drop cancels
        else if (_heldSlot is { } slot)
        {
            var cells = SlotInfo.All.Select(s => (s, r.PartRect((Part)s))).Where(x => x.Item2 is not null)
                .Select(x => (x.s, new Cell(x.Item2!.Value.X, x.Item2.Value.Y, x.Item2.Value.Width, x.Item2.Value.Height)));
            if (SlotDrop.TargetAt(slot, cells, SlotArrangement.Of(_s.Overlay.SlotOrder), at.X, at.Y) is { } t)
                (_slotTarget, anchor, side) = (t, CellOf(r, (Part)t.Anchor), t.Side);
        }
        else if (_heldBar is { } bar)
        {
            var bars = Enum.GetValues<BuffKind>().Select(k => (k, r.PartRect(FreeLayout.FirstBuff + (int)k))).Where(x => x.Item2 is not null)
                .Select(x => (x.k, new Cell(x.Item2!.Value.X, x.Item2.Value.Y, x.Item2.Value.Width, x.Item2.Value.Height)));
            var found = r == _buffs ? BuffStackDrop.TargetAt(bar, bars, _s.Buffs.Upright(_s.Apart), at.X, at.Y)
                : BarDrop.TargetAt(bar, bars, Math.Max(FreeLayout.Gap, ArrangeReach / (_v?.Scale ?? 1)), at.X, at.Y);
            if (found is { } t)
                (_barTarget, anchor, side) = (t, CellOf(r, FreeLayout.FirstBuff + (int)t.Anchor), t.Side);
        }
        _heldSize = HeldSize(r, partSize);
        var (left, top) = (Canvas.GetLeft(r), Canvas.GetTop(r));
        (_ghost.Width, _ghost.Height) = (_heldSize.Width, _heldSize.Height);
        Canvas.SetLeft(_ghost, left + at.X - _heldSize.Width / 2);
        Canvas.SetTop(_ghost, top + at.Y - _heldSize.Height / 2);
        _ghost.Visibility = Visibility.Visible;
        if (anchor is not { } a) { _drop.Visibility = Visibility.Collapsed; return; }
        var m = DropMarker.Of(a, side, FreeLayout.Gap, InsertLinePx / (_v?.Scale ?? 1));
        bool line = side != DropSide.Middle;
        (_drop.Width, _drop.Height) = (m.W, m.H);
        _drop.Fill = line ? _drop.Stroke : null;
        _drop.StrokeThickness = line ? 0 : 3;
        Canvas.SetLeft(_drop, left + m.X);
        Canvas.SetTop(_drop, top + m.Y);
        _drop.Visibility = Visibility.Visible;
    }

    /// <summary>Demo only (<c>--arrange=from,to,fx,fy[,drop]</c>): <paramref name="from"/> held over <paramref name="to"/> at
    /// (fx, fy) of its box, as a mouse would; with <paramref name="drop"/> the drop is made too. For screenshots.</summary>
    internal void DemoArrange(Part from, Part to, double fx, double fy, bool drop)
    {
        if (_mine.PartRect(from) is not { } a || _mine.PartRect(to) is not { } b) return;
        if (!StartArrange(_mine, new(a.X + a.Width / 2, a.Y + a.Height / 2))) return;
        _drag = Drag.Arrange;
        UpdateArrange(_mine, new(b.X + b.Width * fx, b.Y + b.Height * fy));
        if (drop) FinishArrange();
    }

    const double InsertLinePx = 4;   // the gold insert line is this thick on screen at any zoom
    const double ArrangeReach = 8;   // how far past a bar's long side still counts as the gap, on screen

    const double DockReach = 24;     // how far outside the plate a group drag still docks on that side, on screen

    static Cell CellOf(OverlayRenderer r, Part p) => r.PartRect(p) is { } b ? ToCell(b) : default;
    static Cell ToCell(Rect b) => new(b.X, b.Y, b.Width, b.Height);

    /// <summary>The drop: the arrangement the target gives is written as one undo step.</summary>
    void FinishArrange()
    {
        var (slot, bar, slotTarget, barTarget, group, aim) = (_heldSlot, _heldBar, _slotTarget, _barTarget, _heldGroup, _aim);
        EndArrange();
        if (group is { } g && aim is { } a)
        {
            var now = DockOrder.Of(_s.Overlay.DockOrder);
            var order = DockOrder.Format(DockDrop.Apply(now, g, a));
            if (order == DockOrder.Format(now)) return;
            GestureStarted?.Invoke();
            _s.Overlay.DockOrder = order;
        }
        else if (slot is { } s && slotTarget is { } st)
        {
            var order = SlotArrangement.Format(SlotDrop.Apply(SlotArrangement.Of(_s.Overlay.SlotOrder), s, st));
            if (order == SlotArrangement.Format(SlotArrangement.Of(_s.Overlay.SlotOrder))) return;   // dropped where it was: no empty undo step
            GestureStarted?.Invoke();
            _s.Overlay.SlotOrder = order;
        }
        else if (bar is { } b && barTarget is { } bt)
        {
            var rows = BarArrangement.Of(_s.Overlay.BarOrder);
            var order = BarArrangement.Format(_heldInBuffs ? BuffStackDrop.Apply(rows, b, bt, _s.Buffs.Upright(_s.Apart)) : BarDrop.Apply(rows, b, bt));
            if (order == BarArrangement.Format(BarArrangement.Of(_s.Overlay.BarOrder))) return;
            GestureStarted?.Invoke();
            _s.Overlay.BarOrder = order;
        }
        else return;
        Relayout();
        ShowPick();
        Changed?.Invoke();
    }

    void EndArrange()
    {
        _drag = Drag.None;
        (_heldSlot, _heldBar, _slotTarget, _barTarget, _heldGroup, _aim, _groupDrag) = (null, null, null, null, null, null, false);
        _drop.Visibility = _ghost.Visibility = Visibility.Collapsed;
    }

    /// <summary>Esc: the drag is over and nothing changes.</summary>
    void CancelArrange()
    {
        EndArrange();
        ReleaseMouseCapture();
    }
}
