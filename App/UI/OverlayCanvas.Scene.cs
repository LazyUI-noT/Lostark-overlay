using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The scene of 설정 → 표시: the canvas read-only, every window drawn (the DPS 미터기 too) on a copy of the settings,
/// a window that's off in the real settings faint with a dashed outline and its name. A click names the window
/// (<see cref="ScenePicked"/>); nothing drags, pans, zooms or nudges.</summary>
public sealed partial class OverlayCanvas
{
    const double FaintOpacity = 0.3;
    const double FrameStroke = 3;   // screen px: the screen's edge, where the zoom shows it
    // In view pixels, whatever the zoom: an off window's name and outline.
    const double FaintFont = 13, FaintStroke = 1.7;
    // Screen px round the windows in the zoomed scene (user, 2026-11-01: the windows, not the whole screen).
    const double SceneMargin = 60;
    bool _sceneFitted;

    /// <summary>Zooms the scene to the box round every window shown, plus <see cref="SceneMargin"/> (EditorView.Around).</summary>
    void FitScene(IReadOnlyCollection<OverlayRenderer> all)
    {
        var shown = all.Where(r => r.Visibility == Visibility.Visible && r.DesiredSize.Width > 0).ToList();
        if (shown.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        double l = shown.Min(Canvas.GetLeft), t = shown.Min(Canvas.GetTop);
        double r = shown.Max(w => Canvas.GetLeft(w) + w.DesiredSize.Width), b = shown.Max(w => Canvas.GetTop(w) + w.DesiredSize.Height);
        (_v, _sceneFitted) = (EditorView.Around(ActualWidth, ActualHeight, l, t, r - l, b - t, SceneMargin), true);
        ShowView();
    }

    /// <summary>The game screen's edge, so the scene shows where each window stands on it.</summary>
    Rectangle ScreenFrame() => new()
    {
        Width = _screen.Width, Height = _screen.Height, Fill = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)),
        Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x4C, 0x55)), StrokeThickness = FrameStroke, IsHitTestVisible = false,
    };

    /// <summary>Whether a window is on in the real settings (the copy drawn has every window on).</summary>
    public Func<OverlayPart, bool>? IsOn { get; set; }

    /// <summary>A window of the scene was clicked.</summary>
    public event Action<OverlayPart>? ScenePicked;

    /// <summary>The window under the mouse (null: none), so its look can hold while hovered.</summary>
    public event Action<OverlayPart?>? SceneHover;

    /// <summary>The outlines and names of windows that are off, over every window (added last to the world).</summary>
    readonly Canvas _faint = new() { IsHitTestVisible = false };

    OverlayPart WindowPart(OverlayRenderer r) =>
        r == _buffs ? OverlayPart.Buffs : r == _debuffs ? OverlayPart.Debuffs : r == _judgment ? OverlayPart.Judgment : r == _amsu ? OverlayPart.Amsu
        : r == _raid ? OverlayPart.BarCount : r == _meter ? OverlayPart.Meter : r.Peer >= 0 ? OverlayPart.Party : OverlayPart.Skills;

    /// <summary>My window draws three parts: the gauge, the buff bars (not detached) and the skill boxes.</summary>
    OverlayPart ScenePart(OverlayRenderer r, Point at) => r != _mine ? WindowPart(r) : r.PartAt(at)?.Part switch
    {
        Part.게이지 => OverlayPart.Gauge,
        >= FreeLayout.FirstBuff => OverlayPart.Buffs,
        _ => OverlayPart.Skills,
    };

    void OnSceneDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not OverlayRenderer r) return;
        ScenePicked?.Invoke(ScenePart(r, e.GetPosition(r)));
        e.Handled = true;
    }

    void OnSceneMove(object sender, MouseEventArgs e)
    {
        var r = e.OriginalSource as OverlayRenderer;
        Cursor = r is null ? null : Cursors.Hand;
        SceneHover?.Invoke(r is null ? null : ScenePart(r, e.GetPosition(r)));
    }

    /// <summary>Every window shown (the buffs' own window only while detached), before they are measured.</summary>
    void SceneVisibility(IEnumerable<OverlayRenderer> all)
    {
        foreach (var r in all) r.Visibility = r == _buffs ? Shown(_s.Buffs.Detached) : Visibility.Visible;
    }

    /// <summary>Windows off in the real settings: faint, outlined, named. Party windows: one name on the first.</summary>
    void ShowFaint(IEnumerable<OverlayRenderer> all)
    {
        _faint.Children.Clear();
        _world.Children.Remove(_faint);
        _world.Children.Add(_faint);   // last: over the windows
        foreach (var r in all)
        {
            var part = WindowPart(r);
            bool on = IsOn?.Invoke(part) ?? true;
            if (r == _mine) on |= IsOn?.Invoke(OverlayPart.Gauge) ?? true;   // faint only when the skills and the gauge are both off
            r.Opacity = on ? 1 : FaintOpacity;
            if (on || r.Visibility != Visibility.Visible) continue;
            var (l, t, size) = (Canvas.GetLeft(r), Canvas.GetTop(r), r.DesiredSize);
            double perView = 1 / (_v?.Scale ?? 1);   // view px to screen px at this zoom
            var outline = new Rectangle
            {
                Width = size.Width, Height = size.Height, Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = FaintStroke * perView, StrokeDashArray = [2, 1.5], IsHitTestVisible = false,
            };
            Add(outline, l, t);
            if (r.Peer > 0) continue;
            string name = Tray.PartNames.First(n => n.Part == part).Name;
            Add(new TextBlock
            {
                Text = $" {name} · 꺼짐 ", Foreground = Brushes.White, FontSize = FaintFont * perView, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)),
            }, l, t - FaintFont * perView * 1.4);
        }
    }

    void Add(UIElement e, double left, double top)
    {
        Canvas.SetLeft(e, left);
        Canvas.SetTop(e, top);
        _faint.Children.Add(e);
    }
}
