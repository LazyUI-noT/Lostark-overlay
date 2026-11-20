using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>A 설정 page's 게임 화면 preview at actual size (preview-images.plan.md §3, <see cref="Core.Domain.PreviewView"/>):
/// a drag pans the view, or with 창 위치 옮기기 moves the page's window; the wheel zooms. No picture area (a backdrop only):
/// the old fit round the windows, no pan or zoom.</summary>
public sealed partial class OverlayCanvas
{
    const double DragSlop = 4;   // view px a press may wander and still be a click
    const double OthersOpacity = 0.45;   // 다른 오버레이 표시: the profile's other windows, dimmed
    PreviewView? _pv;
    Point? _downAt;   // the press, while the left button is down on the preview
    Point _lastAt;
    bool _dragging;
    object? _downSource;   // what was pressed, played as a click at _downAt when it doesn't become a drag
    (OverlayRenderer R, double L, double T)? _moveFrom;   // 창 위치 옮기기: the window and its spot when the drag began

    /// <summary>The preview's view (null: no picture area, or not a preview).</summary>
    public PreviewView? View => _pv;

    /// <summary>The left button is down on the preview (a click or a drag under way).</summary>
    public bool Pressed => _downAt is not null;

    /// <summary>창 위치 옮기기: a drag moves the page's window (one undo step, <see cref="GestureStarted"/>) instead of the view.</summary>
    public bool MovesWindow { get; set; }

    /// <summary>A drag moved the page's window on this canvas's copy; read the spots back with <see cref="WindowSpot.Get"/>.</summary>
    public event Action? WindowMoved;

    /// <summary>다른 오버레이 표시: these windows drawn too, dimmed, not clickable (null: none).</summary>
    public IReadOnlyCollection<OverlayPart>? Others { get; set; }

    bool Dimmed(OverlayRenderer r) => Others is not null && !InScene(r, Only);

    /// <summary>How many of <see cref="Others"/> draw something (after <see cref="Relayout"/>).</summary>
    public int DimmedShown => All.Count(r => r.Visibility == Visibility.Visible && r.DesiredSize.Width > 0 && Dimmed(r));

    /// <summary>The picture's rect on screen (DIP; null: a backdrop only), the zoom kept from the last preview, centred on the
    /// page's window. Call once the windows are placed (after <see cref="Relayout"/>).</summary>
    public void ViewPicture(ViewRect? picture, double zoom)
    {
        if (picture is not { } p || ActualWidth <= 0)
        {
            _pv = null;
            return;
        }
        _pv = new PreviewView(p, ActualWidth, ActualHeight);
        _pv.ZoomBy(zoom);
        ReturnToWindow();
    }

    /// <summary>창으로 돌아가기: the view centred on the page's window again.</summary>
    public void ReturnToWindow()
    {
        if (_pv is null) return;
        if (PageWindow() is { } w) _pv.CentreOn(Bounds(w));
        ApplyView();
    }

    public void ZoomPreview(double factor)
    {
        if (_pv is null) return;
        _pv.ZoomBy(factor);
        ApplyView();
    }

    /// <summary>The view drawn after <see cref="View"/> changed (the minimap moves it too).</summary>
    public void ApplyView()
    {
        if (_pv is null) return;
        var (scale, x, y) = _pv.Transform;
        _v = new EditorView(scale, x, y);
        ShowView();
        ZoomFaint();
    }

    /// <summary>What the minimap shows: the picture, else the backdrop's fill.</summary>
    public Brush? Backdrop => _shot.Source is { } s ? new ImageBrush(s) : _frame?.Fill;

    static ViewRect Bounds(OverlayRenderer r) => new(Canvas.GetLeft(r), Canvas.GetTop(r), r.DesiredSize.Width, r.DesiredSize.Height);

    /// <summary>The page's first window drawn (mine before the others), the one 창 위치 옮기기 drags.</summary>
    OverlayRenderer? PageWindow() => All.FirstOrDefault(r => r.Visibility == Visibility.Visible && r.DesiredSize.Width > 0 && !Dimmed(r));

    /// <summary>A press on a preview at actual size: a click (played on release) or the start of a drag.</summary>
    bool PreviewDown(MouseButtonEventArgs e)
    {
        if (_pv is null) return false;
        Focus();
        _downAt = _lastAt = e.GetPosition(this);
        (_dragging, _downSource, _moveFrom) = (false, e.OriginalSource, null);
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    void PreviewMove(MouseEventArgs e)
    {
        if (_downAt is not { } down || _pv is null || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(this);
        if (!_dragging && (at - down).Length < DragSlop) return;
        _dragging = true;
        Cursor = Cursors.SizeAll;
        if (MovesWindow && (_moveFrom ?? StartMove()) is var (r, l, t))
        {
            var (dx, dy) = _pv.ToWorld(at.X - down.X, at.Y - down.Y);
            SetSpot(r, l + dx, t + dy);
            Relayout();   // ponytail: redraws every window per mouse move; move only this one if drags stutter
            _pv.CentreOn(Bounds(r));
            ApplyView();
            WindowMoved?.Invoke();
        }
        else if (!MovesWindow)
        {
            _pv.Pan(at.X - _lastAt.X, at.Y - _lastAt.Y);
            ApplyView();
        }
        _lastAt = at;
    }

    (OverlayRenderer, double, double)? StartMove()
    {
        if (PageWindow() is not { } r) return null;
        GestureStarted?.Invoke();
        var (l, t) = Spot(r);
        _moveFrom = (r, l, t);
        return _moveFrom;
    }

    /// <summary>The press ends: a left release plays it as a click unless it was dragged; a lost capture (alt-tab, rebuild) only ends it.</summary>
    void PreviewUp(bool play)
    {
        if (_downAt is not { } at) return;
        var source = play && !_dragging ? _downSource : null;
        (_downAt, _downSource, _moveFrom, _dragging) = (null, null, null, false);
        ReleaseMouseCapture();
        Cursor = null;
        if (source is not null) SceneClick(source, at);
    }
}
