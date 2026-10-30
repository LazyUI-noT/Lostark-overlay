using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The canvas's mouse and keys: pan, move a window, scale it, move or resize a box, resize a debuff box, nudge.</summary>
public sealed partial class OverlayCanvas
{
    const double NudgeStep = 1, BigNudgeStep = 10;

    enum Drag { None, Pan, Move, Scale, Box, BoxSize, DebuffSize }
    Drag _drag;
    bool _gestureRaised;   // GestureStarted already said for this drag
    Point _from;
    OverlayRenderer? _target;
    (double L, double T) _startSpot;
    (double W, double H) _startSize, _startPlate;
    Part _part;
    Dictionary<Part, Cell> _startCells = [];   // the fixed grid stays null until the box really moves
    Part? _still;                              // Box, BoxSize: a box the drag doesn't move, kept where it was on screen
    Point _stillAt;
    int _debuffIndex;                                       // DebuffSize: the box dragged
    (double W, double H) _startBox, _startDrawn;           // its size setting and drawn size when the drag began

    IWindowPlace SizeOf(OverlayRenderer r) => r == _buffs ? _s.Buffs : r == _debuffs ? _s.Debuffs : r == _judgment ? _s.Judgment : r == _raid ? _s.Raids : _s.Overlay;

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        Note?.Invoke(null);   // a new click: any refusal is over
        // a right or middle drag pans anywhere, a left one on the picture (not on an overlay)
        if (e.ChangedButton is MouseButton.Right or MouseButton.Middle || e.ChangedButton == MouseButton.Left && e.OriginalSource is not OverlayRenderer)
        {
            (_drag, _from) = (Drag.Pan, e.GetPosition(this));
            CaptureMouse();
            if (e.ChangedButton == MouseButton.Left) Pick(null, null);   // an empty-area click picks nothing
            return;
        }
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not OverlayRenderer r) return;
        var at = e.GetPosition(r);
        if (r == _debuffs) PickDebuff(r.DebuffAt(at));
        else Pick(r, r.PartAt(at)?.Part);
        if (Play.Running)   // ▶: a click uses the box instead of moving it
        {
            if (_picked is ({ Peer: < 0 }, { } used)) Play.Click(used, PreviewPlayer.Sections(_s, _chars));
            e.Handled = true;
            return;
        }
        _target = r;
        _gestureRaised = false;
        _from = e.GetPosition(_world);
        _startSpot = Spot(r);
        bool corner = at.X >= r.ActualWidth - Bleed - Grip && at.Y >= r.ActualHeight - Bleed - Grip;
        _drag = corner && r.Peer < 0 ? Drag.Scale : Drag.Move;   // a member's size is its class's
        if (r.ShowsReceivedLayout || Previewing(r))   // a box drag would go into a layout that isn't the one drawn: the window moves
        {
            if (r.PartAt(at) is not null) Note?.Invoke(r.ShowsReceivedLayout ? ReceivedNote : PreviewNote);
        }
        else if (r == _debuffs)
        {
            // a debuff box grows by its corner; the rest of the window still drags it (the boxes stand in a fixed row)
            if (r.DebuffAt(at) is { } i && r.DebuffRect(i) is var box && InGrip(box, at))
            {
                (_drag, _debuffIndex) = (Drag.DebuffSize, i);
                _startBox = (_s.Debuffs.Box(i).Width, _s.Debuffs.Box(i).Height);
                _startDrawn = (box.Width, box.Height);
            }
        }
        else if (r != _buffs && r.PartAt(at) is var (part, rect))   // the buff window is the fixed grid
        {
            _drag = InGrip(rect, at) ? Drag.BoxSize : Drag.Box;
            StartBoxDrag(r, part, _drag == Drag.BoxSize);
        }
        _startSize = (SizeOf(r).Width, SizeOf(r).Height);
        _startPlate = (r.ActualWidth - 2 * Bleed, r.ActualHeight - 2 * Bleed);
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>A box drag (or nudge) of <paramref name="part"/> begins: its cells as they stand, and a box it won't move to
    /// hold the window still by (<see cref="KeepStill"/>).</summary>
    void StartBoxDrag(OverlayRenderer r, Part part, bool resize)
    {
        _part = part;
        _startCells = new(Boxes(r).Layout ?? r.CellsAsDrawn());
        var moving = resize && _s.Overlay.LayoutLinkSizes ? FreeLayout.Linked(part).Parts : [part];
        _still = r.DrawnParts.Where(q => !moving.Contains(q)).Cast<Part?>().FirstOrDefault();
        if (_still is { } p && OnCanvas(r, p) is { } at0) _stillAt = at0;
        else _still = null;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (_drag == Drag.None)
        {
            Cursor = OverGrip(e) ? Cursors.SizeNWSE : null;
            return;
        }
        if (_drag == Drag.Pan)
        {
            var at = e.GetPosition(this);
            _v?.Pan(at.X - _from.X, at.Y - _from.Y);
            _from = at;
            ShowView();
            return;
        }
        if (_target is not { } r) return;
        var now = e.GetPosition(_world);
        double dx = now.X - _from.X, dy = now.Y - _from.Y;
        if ((dx, dy) == (0, 0)) return;
        if (!_gestureRaised) { _gestureRaised = true; GestureStarted?.Invoke(); }
        switch (_drag)
        {
            case Drag.Move:
                SetSpot(r, _startSpot.L + dx, _startSpot.T + dy);
                break;
            case Drag.DebuffSize:
                var sized = _s.Debuffs.Box(_debuffIndex);
                (sized.Width, sized.Height) = BarSizes.DebuffResized(_startBox, _startDrawn, dx, dy);
                break;
            case Drag.Scale:
                var place = SizeOf(r);
                (place.Width, place.Height) = OverlayRenderer.Resized(_startSize, _startPlate, dx, dy);
                break;
            default:
                MoveBox(r, dx, dy, resize: _drag == Drag.BoxSize,
                    snap: _s.Overlay.LayoutSnap && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift));
                Changed?.Invoke();
                return;
        }
        Relayout();
        Changed?.Invoke();
    }

    /// <summary>The box being dragged, <paramref name="dx"/>, <paramref name="dy"/> from where it started.</summary>
    void MoveBox(OverlayRenderer r, double dx, double dy, bool resize, bool snap)
    {
        var scale = r.CellScale;
        if (resize && _part >= FreeLayout.FirstBuff && _startCells.TryGetValue(_part, out var cell))
        {
            // The bar is drawn at its cell × 길이 × 두께: grow the cell so the drawn corner follows the pointer.
            var bar = _s.Buffs.Of((BuffKind)(_part - FreeLayout.FirstBuff));
            var (fx, fy) = BarSizes.DragFactors(cell, BarSizes.Length(bar), BarSizes.Thickness(bar));
            scale = (scale.X * fx, scale.Y * fy);
        }
        Boxes(r).Layout = FreeLayout.Drag(_startCells, r.DrawnParts, _part, dx, dy, scale, resize, snap, link: _s.Overlay.LayoutLinkSizes);
        Relayout();
        KeepStill(r);
    }

    void OnUp()
    {
        if (_drag == Drag.DebuffSize) Selected?.Invoke(new("Debuff", null, _debuffIndex));   // its sliders show the new size
        ReleaseMouseCapture();
    }

    /// <summary>Arrow keys nudge the picked box, or the window when its plate is picked (Shift: 10 px).</summary>
    void OnKey(object sender, KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? BigNudgeStep : NudgeStep;
        (double dx, double dy) = e.Key switch
        {
            Key.Left => (-step, 0d), Key.Right => (step, 0d), Key.Up => (0d, -step), Key.Down => (0d, step), _ => (0d, 0d),
        };
        if ((dx, dy) == (0, 0) || _drag != Drag.None || Play.Running) return;
        e.Handled = true;
        OverlayRenderer? r = _pickedDebuff is not null ? _debuffs : _picked?.R;
        if (r is null || r.Visibility != Visibility.Visible) return;
        GestureStarted?.Invoke();
        if (_pickedDebuff is null && _picked?.Part is { } part && r != _buffs && r != _judgment && r != _raid && !r.ShowsReceivedLayout && !Previewing(r))
        {
            StartBoxDrag(r, part, resize: false);
            MoveBox(r, dx, dy, resize: false, snap: false);
        }
        else
        {
            var (l, t) = Spot(r);
            SetSpot(r, l + dx, t + dy);
            Relayout();
        }
        Changed?.Invoke();
    }

    /// <summary>The plate is drawn from its top-left box, so a box dragged above or left of the others would push them
    /// down or right; the window moves back by as much, as <see cref="OverlayWindow.Refresh"/> does with its anchor.</summary>
    void KeepStill(OverlayRenderer r)
    {
        if (_still is not { } p || OnCanvas(r, p) is not { } at) return;
        double dx = at.X - _stillAt.X, dy = at.Y - _stillAt.Y;
        if ((dx, dy) == (0, 0)) return;
        var (l, t) = Spot(r);
        SetSpot(r, l - dx, t - dy);
        Relayout();
    }

    /// <summary>A box's corner grip (its bottom-right square, which resizes it): <see cref="Grip"/> on screen at any zoom,
    /// at most half the box so the rest still moves it.</summary>
    double GripSize(Rect box) => Math.Min(Grip / (_v?.Scale ?? 1), Math.Min(box.Width, box.Height) / 2);

    bool InGrip(Rect box, Point at) => GripSize(box) is var g && at.X >= box.Right - g && at.Y >= box.Bottom - g && box.Contains(at);

    /// <summary>The pointer is on a box's corner grip that a drag would resize.</summary>
    bool OverGrip(MouseEventArgs e)
    {
        if (Play.Running || e.OriginalSource is not OverlayRenderer r || r == _buffs || r == _judgment || r == _raid || r.ShowsReceivedLayout || Previewing(r)) return false;
        var at = e.GetPosition(r);
        if (r == _debuffs) return r.DebuffAt(at) is { } i && InGrip(r.DebuffRect(i), at);
        return r.PartAt(at) is var (_, rect) && InGrip(rect, at);
    }

    /// <summary>My window drawn as another 직업각인 (편집 중): its boxes are that one's sample, but a drag would write the
    /// character's layout (<see cref="Boxes"/>), so boxes don't drag; the window still moves.</summary>
    bool Previewing(OverlayRenderer r) => r == _mine && _previewTarget is not null;

    static Point? OnCanvas(OverlayRenderer r, Part p) =>
        r.PartRect(p) is { } rect ? new Point(Canvas.GetLeft(r) + rect.X, Canvas.GetTop(r) + rect.Y) : null;
}
