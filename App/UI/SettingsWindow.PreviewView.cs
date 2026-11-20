using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>The preview at actual size (preview-images.plan.md §3): the minimap, 창 위치 옮기기, 창으로 돌아가기, zoom and
/// 다른 오버레이 표시. Zoom and pan are the view only (not saved, no undo step); the zoom stays across rebuilds while 설정 is open.</summary>
public partial class SettingsWindow
{
    const double MinimapWidth = 96, MinimapMargin = 6;
    bool _previewMoves, _previewOthers;
    double _previewZoom = 1;
    // Beside the controls, not over the preview: over it, it hid the picture's top-right (파티원 모양's 미터기).
    readonly Border _minimap = new() { VerticalAlignment = VerticalAlignment.Top, Margin = new(MinimapMargin, 0, 0, 0), BorderThickness = new(1), Cursor = Cursors.Hand };
    readonly TextBlock _previewOthersNote = Note("표시가 켜진 다른 창이 없습니다.");
    CheckBox? _previewMovesCheck, _previewOthersCheck;
    readonly Rectangle _minimapBox = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false };
    readonly TextBlock _previewZoomText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center };
    readonly TextBlock _previewAreaNote = Note("모니터와 모양이 다른 그림: 배경으로만 보여 주며 확대하거나 옮길 수 없습니다.");
    UIElement? _previewViewRow;
    Point? _minimapAt;   // while the box is dragged: the pointer's offset from the box's centre

    /// <summary>Under the preview: the drag switch, 창으로 돌아가기, zoom and 다른 오버레이 표시.</summary>
    UIElement PreviewViewControls()
    {
        _minimap.Child = new Canvas { ClipToBounds = true, Children = { _minimapBox } };
        _minimap.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        _minimap.MouseLeftButtonDown += MinimapDown;
        _minimap.MouseMove += MinimapMove;
        _minimap.MouseLeftButtonUp += (_, _) => _minimap.ReleaseMouseCapture();
        _minimap.LostMouseCapture += (_, _) => _minimapAt = null;
        System.Windows.Automation.AutomationProperties.SetName(_minimap, "미니맵");

        var moves = Check("창 위치 옮기기", _previewMoves, v => _previewMoves = v, () => { if (_preview is not null) _preview.MovesWindow = _previewMoves; },
            "켜면 끌어서 창을 옮깁니다. 끄면 그림만 움직이고 창은 그대로입니다.");
        var back = SmallButton("창으로 돌아가기", () => _preview?.ReturnToWindow());
        var zoomOut = SmallButton("−", () => _preview?.ZoomPreview(1 / PreviewView.ZoomStep));
        var zoomIn = SmallButton("+", () => _preview?.ZoomPreview(PreviewView.ZoomStep));
        System.Windows.Automation.AutomationProperties.SetName(zoomOut, "축소");
        System.Windows.Automation.AutomationProperties.SetName(zoomIn, "확대");
        var row = new WrapPanel { Children = { moves, back, zoomOut, _previewZoomText, zoomIn } };
        foreach (FrameworkElement e in row.Children) e.Margin = new(0, 0, Tokens.Space.Gap, Tokens.Space.Hair);
        _previewViewRow = row;
        var others = Check("다른 오버레이 표시", _previewOthers, v => _previewOthers = v, () => { if (_preview is not null) BuildPreview(); },
            "표시가 켜진 다른 창도 흐리게 보여 줍니다. 여기서는 고르거나 옮길 수 없습니다.");
        (_previewMovesCheck, _previewOthersCheck) = (moves, others);
        _previewAreaNote.Visibility = _previewOthersNote.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(_minimap, Dock.Right);
        return new DockPanel
        {
            Margin = new(0, Tokens.Space.Gap, 0, 0),
            Children = { _minimap, new StackPanel { Children = { row, _previewAreaNote, others, _previewOthersNote } } },
        };
    }

    /// <summary>--demo --preview-others, --preview-zoom=N (percent), --preview-move: the preview's switches as if set, for shot.ps1.</summary>
    internal void DemoPreview(bool others, double? zoomPercent, bool moves)
    {
        (_previewOthers, _previewMoves) = (others, moves);
        if (_previewOthersCheck is not null) _previewOthersCheck.IsChecked = others;
        if (_previewMovesCheck is not null) _previewMovesCheck.IsChecked = moves;
        if (zoomPercent is { } z) _previewZoom = z / 100;
        if (_preview is not null) BuildPreview();
    }

    static Button SmallButton(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new(Tokens.Space.Gap, Tokens.Space.Hair, Tokens.Space.Gap, Tokens.Space.Hair) };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>The new preview's view switches and events (<see cref="BuildPreview"/>), before it loads.</summary>
    void HookPreviewView(OverlayCanvas preview, Settings copy)
    {
        preview.MovesWindow = _previewMoves;
        preview.Others = _previewOthers ? SettingsPreview.Others(_previewParts, p => TrayToggles.IsShown(_s, p)) : null;
        preview.GestureStarted += RecordStep;   // one drag = one undo step
        preview.WindowMoved += () =>
        {
            foreach (var part in _previewParts)   // only the dragged window changed; the others copy back as they were
            {
                var (l, t) = WindowSpot.Get(copy, part);
                WindowSpot.Set(Target, part, l, t);
            }
            KeepCopy();
            ResetBaseline();
            _canvas?.Relayout();
            SettingsChanged?.Invoke();
            LayoutChanged?.Invoke();
        };
        preview.ViewChanged += () => PreviewViewShown(preview);
    }

    /// <summary>Once placed: the view at actual size over <paramref name="picture"/> (null: a backdrop only, fitted as before).</summary>
    void StartPreviewView(OverlayCanvas preview, ViewRect? picture)
    {
        preview.ViewPicture(picture, _previewZoom);
        bool view = preview.View is not null;
        _previewViewRow!.Visibility = Shown(view);
        _previewAreaNote.Visibility = Shown(picture is null);
        _minimap.Visibility = Shown(view);
        _previewOthersNote.Visibility = Shown(_previewOthers && preview.DimmedShown == 0);   // else the switch would seem to do nothing
        if (picture is not { } p || !view) return;
        _minimap.Background = preview.Backdrop;
        (_minimap.Width, _minimap.Height) = (MinimapWidth, MinimapWidth * p.Height / Math.Max(1, p.Width));
        PreviewViewShown(preview);
    }

    void PreviewViewShown(OverlayCanvas preview)
    {
        if (preview != _preview || preview.View is not { } v) return;
        _previewZoom = v.Zoom;
        _previewZoomText.Text = $"{v.Zoom * 100:0}%";
        var (w, h) = MinimapInside();
        if (w <= 0) return;
        var box = v.MinimapBox(w, h);
        Canvas.SetLeft(_minimapBox, box.X);
        Canvas.SetTop(_minimapBox, box.Y);
        (_minimapBox.Width, _minimapBox.Height) = (Math.Max(2, box.Width), Math.Max(2, box.Height));
    }

    (double W, double H) MinimapInside() => double.IsNaN(_minimap.Width) ? (0, 0) : (_minimap.Width - 2, _minimap.Height - 2);   // inside the border

    /// <summary>A press on the minimap: off the box, the view jumps there; then the box drags with the pointer. Only the view moves.</summary>
    void MinimapDown(object sender, MouseButtonEventArgs e)
    {
        var (w, h) = MinimapInside();
        if (_preview?.View is not { } v || w <= 0) return;
        var at = e.GetPosition((IInputElement)_minimap.Child);
        var box = v.MinimapBox(w, h);
        var centre = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        if (!new Rect(box.X, box.Y, box.Width, box.Height).Contains(at))
        {
            v.JumpTo(at.X / w, at.Y / h);
            _preview.ApplyView();
            centre = at;
        }
        _minimapAt = new Point(at.X - centre.X, at.Y - centre.Y);   // the grab, kept so the box stays under the pointer past an edge
        _minimap.CaptureMouse();
        e.Handled = true;
    }

    void MinimapMove(object sender, MouseEventArgs e)
    {
        var (w, h) = MinimapInside();
        if (_minimapAt is not { } grab || _preview?.View is not { } v || w <= 0) return;
        var at = e.GetPosition((IInputElement)_minimap.Child);
        v.JumpTo((at.X - grab.X) / w, (at.Y - grab.Y) / h);
        _preview.ApplyView();
    }
}
