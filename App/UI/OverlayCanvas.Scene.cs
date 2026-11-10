using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The scene of 설정 → 기능 Map: the canvas read-only, every window drawn (the DPS 미터기 too) on a copy of the settings,
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
        // A page's preview fits round the windows that are on: 레이드's 줄 수 창 (top) and an off 디버프 창 (bottom) zoomed out to specks.
        if (Only is not null && IsOn is { } isOn && shown.Where(r => isOn(WindowPart(r)) || r == _mine && isOn(OverlayPart.Gauge)).ToList() is { Count: > 0 } on)
            shown = on;
        if (shown.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        double l = shown.Min(Canvas.GetLeft), t = shown.Min(Canvas.GetTop);
        double r = shown.Max(w => Canvas.GetLeft(w) + w.DesiredSize.Width), b = shown.Max(w => Canvas.GetTop(w) + w.DesiredSize.Height);
        (_v, _sceneFitted) = (EditorView.Around(ActualWidth, ActualHeight, l, t, r - l, b - t, SceneMargin), true);
        ShowView();
    }

    /// <summary>The game screen's edge, so the scene shows where each window stands on it.</summary>
    /// <summary>Behind the windows when no game shot is saved: a dim game-like scene (muted sky into darker ground), mid-tone
    /// so a window's dark tracks and its white text both read as they do over the game (user, 2026-11-09: near-black hid them).</summary>
    static readonly LinearGradientBrush GameBackdrop = Frozen(new LinearGradientBrush(
        [new(Color.FromRgb(0x6A, 0x6C, 0x6E), 0), new(Color.FromRgb(0x58, 0x55, 0x4E), 0.55), new(Color.FromRgb(0x3C, 0x36, 0x2E), 1)], 90));

    static T Frozen<T>(T brush) where T : Freezable { brush.Freeze(); return brush; }

    Rectangle ScreenFrame() => new()
    {
        Width = _screen.Width, Height = _screen.Height, Fill = GameBackdrop,
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

    /// <summary>Only these windows drawn (a 설정 page's preview, <see cref="SettingsPreview.PartsOf"/>); null: every window.</summary>
    public IReadOnlyCollection<OverlayPart>? Only { get; set; }

    /// <summary>My window holds the skills, the gauge and the buff bars while they aren't detached.</summary>
    bool InScene(OverlayRenderer r) => Only is not { } only || (r == _mine
        ? only.Contains(OverlayPart.Skills) || only.Contains(OverlayPart.Gauge) || only.Contains(OverlayPart.Buffs) && !_s.Buffs.Detached
        : only.Contains(WindowPart(r)));

    /// <summary>Every window shown (the buffs' own window only while detached), before they are measured.</summary>
    void SceneVisibility(IEnumerable<OverlayRenderer> all)
    {
        foreach (var r in all) r.Visibility = Shown((r != _buffs || _s.Buffs.Detached) && InScene(r));
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
