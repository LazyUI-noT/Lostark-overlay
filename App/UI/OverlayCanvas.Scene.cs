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
    const double PreviewMargin = 16, PreviewMaxScale = 3;   // a page's preview: close round its windows, up to 3×
    bool _sceneFitted;
    Rectangle? _frame;   // the scene's screen: its fill is the backdrop (ShowColour paints it)

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
        // A page's preview zooms in close on its windows (user, 2026-11-10); the 기능 맵 scene keeps its room and 100%.
        var view = Only is null ? EditorView.Around(ActualWidth, ActualHeight, l, t, r - l, b - t, SceneMargin)
            : EditorView.Around(ActualWidth, ActualHeight, l, t, r - l, b - t, PreviewMargin, PreviewMaxScale);
        (_v, _sceneFitted) = (view, true);
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
        r == _buffs ? OverlayPart.Buffs : r == _gauge ? OverlayPart.Gauge : r == _debuffs ? OverlayPart.Debuffs : r == _judgment ? OverlayPart.Judgment : r == _amsu ? OverlayPart.Amsu
        : r == _raid ? OverlayPart.BarCount : r == _meter ? OverlayPart.Meter : r.Peer >= 0 ? OverlayPart.Party : OverlayPart.Skills;

    /// <summary>My window draws three parts: the gauge, the buff bars (not detached) and the skill boxes.</summary>
    OverlayPart ScenePart(OverlayRenderer r, Point at) => r != _mine ? WindowPart(r) : r.PartAt(at)?.Part switch
    {
        Part.게이지 => OverlayPart.Gauge,
        >= FreeLayout.FirstBuff => OverlayPart.Buffs,
        _ => OverlayPart.Skills,
    };

    /// <summary>A 설정 page's preview: a click moves the animation on (<see cref="PlayClick"/>) instead of raising
    /// <see cref="ScenePicked"/> (user, 2026-11-10; 위치 조정 is a button under it).</summary>
    public bool ClickPlays { get => _clickPlays; set => (_clickPlays, Focusable) = (value, value); }
    bool _clickPlays;

    void OnSceneDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (ClickPlays && e.OriginalSource is not OverlayRenderer)   // the backdrop: every window moves on, even one drawing nothing now (암수)
        {
            Focus();
            StepAll();
            e.Handled = true;
            return;
        }
        if (e.OriginalSource is not OverlayRenderer r) return;
        if (ClickPlays) { Focus(); PlayClick(r, e.GetPosition(r)); }
        else ScenePicked?.Invoke(ScenePart(r, e.GetPosition(r)));
        e.Handled = true;
    }

    /// <summary>A box (skill, buff, gauge) acts as if used: ready starts its cooldown, cooling jumps to its end. A looping
    /// window (디버프, 단심, 암수) jumps to its next phase.</summary>
    void PlayClick(OverlayRenderer r, Point at)
    {
        if (r == _gauge) Play.Click(Part.게이지, PreviewPlayer.Gauge(_s, _chars));
        else if ((r == _mine || r == _buffs) && r.PartAt(at)?.Part is { } part) Play.Click(part, PreviewPlayer.Gauge(_s, _chars));
        else Play.Skip(WindowPart(r));
    }

    /// <summary>What a click on <paramref name="r"/> does something to: my window and the buff window (their boxes) and the
    /// looping ones. The 미터기, 줄 수 창 and 파티원 창 only show the sample.</summary>
    bool PlaysOnClick(OverlayRenderer r) => r == _mine || r == _buffs || r == _gauge || r == _debuffs || r == _judgment || r == _amsu;

    /// <summary>Space or Enter on a focused preview: each of its windows moves on, as a click would (the first skill box, the
    /// gauge, the first buff bar, a loop's next phase).</summary>
    void OnSceneKey(object sender, KeyEventArgs e)
    {
        if (!ClickPlays || e.Key is not (Key.Space or Key.Enter)) return;
        StepAll();
        e.Handled = true;
    }

    /// <summary>Space, Enter or a click on the backdrop: every window of the preview moves on.</summary>
    void StepAll()
    {
        var gauge = PreviewPlayer.Gauge(_s, _chars);
        foreach (var part in Only ?? [])
        {
            switch (part)
            {
                case OverlayPart.Skills: Play.Click(Part.일반1, gauge); break;
                case OverlayPart.Gauge: Play.Click(Part.게이지, gauge); break;
                case OverlayPart.Buffs: Play.Click(FreeLayout.FirstBuff, gauge); break;
                default: Play.Skip(part); break;
            }
        }
    }

    void OnSceneMove(object sender, MouseEventArgs e)
    {
        var r = e.OriginalSource as OverlayRenderer;
        Cursor = ClickPlays ? (r is null || PlaysOnClick(r) ? Cursors.Hand : null) : r is null ? null : Cursors.Hand;
        SceneHover?.Invoke(r is null ? null : ScenePart(r, e.GetPosition(r)));
    }

    /// <summary>Only these windows drawn (a 설정 page's preview, <see cref="SettingsPreview.PartsOf"/>); null: every window.</summary>
    public IReadOnlyCollection<OverlayPart>? Only { get; set; }

    /// <summary>My window holds the skills, the gauge and the buff bars while they aren't detached (합쳐서 / 따로).</summary>
    bool InScene(OverlayRenderer r) => Only is not { } only || (r == _mine
        ? only.Contains(OverlayPart.Skills) || only.Contains(OverlayPart.Gauge) && !_s.Apart || only.Contains(OverlayPart.Buffs) && !_s.Buffs.Detached
        : only.Contains(WindowPart(r)));

    /// <summary>Every window shown (the buffs' own window only while detached), before they are measured.</summary>
    void SceneVisibility(IEnumerable<OverlayRenderer> all)
    {
        foreach (var r in all) r.Visibility = Shown((r != _buffs || _s.Buffs.Detached) && (r != _gauge || _s.Apart) && InScene(r));
    }

    /// <summary>Whether the window is on in the real settings; mine is faint only when the skills and the gauge are both off.</summary>
    bool WindowOn(OverlayRenderer r) =>
        (IsOn?.Invoke(WindowPart(r)) ?? true) || r == _mine && (IsOn?.Invoke(OverlayPart.Gauge) ?? true);

    /// <summary>A window is drawn when it applies (<paramref name="exists"/>) and is on, or off with 꺼진 창 shown.</summary>
    Visibility Drawn(OverlayRenderer r, bool exists) => Shown(exists && (_showOff || WindowOn(r)));

    /// <summary>Windows off in the real settings: faint, outlined, named. Party windows: one name on the first.</summary>
    void ShowFaint(IEnumerable<OverlayRenderer> all)
    {
        _faint.Children.Clear();
        _world.Children.Remove(_faint);
        _world.Children.Add(_faint);   // last: over the windows
        foreach (var r in all)
        {
            var part = WindowPart(r);
            bool on = WindowOn(r);
            r.Opacity = on ? 1 : FaintOpacity;
            if (r.Visibility != Visibility.Visible) continue;
            var (l, t, size) = (Canvas.GetLeft(r), Canvas.GetTop(r), r.DesiredSize);
            double perView = 1 / (_v?.Scale ?? 1);   // view px to screen px at this zoom
            if (on)
            {
                // The skill window is named while on too: users couldn't find 스킬창 in the picture (user, 2026-11-13).
                if (r == _mine) AddName(" 스킬창 ", l, t, perView);
                continue;
            }
            var outline = new Rectangle
            {
                Width = size.Width, Height = size.Height, Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = FaintStroke * perView, StrokeDashArray = [2, 1.5], IsHitTestVisible = false,
            };
            Add(outline, l, t);
            if (r.Peer > 0) continue;
            string name = Tray.PartNames.First(n => n.Part == part).Name;
            AddName($" {name} · 꺼짐 ", l, t, perView);
        }
    }

    /// <summary>A window's name just above its top-left corner (<see cref="ZoomFaint"/> keeps it there).</summary>
    void AddName(string text, double l, double t, double perView) =>
        Add(new TextBlock
        {
            Text = text, Foreground = Brushes.White, FontSize = FaintFont * perView, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), Tag = t,
        }, l, t - FaintFont * perView * 1.4);

    /// <summary>The faint outlines and names at a new zoom: only their stroke, font and the name's lift change, so a wheel
    /// tick doesn't rebuild them (<see cref="ShowFaint"/>).</summary>
    void ZoomFaint()
    {
        double perView = 1 / (_v?.Scale ?? 1);
        foreach (var e in _faint.Children)
        {
            if (e is Rectangle outline) outline.StrokeThickness = FaintStroke * perView;
            else if (e is TextBlock { Tag: double t } name)
            {
                name.FontSize = FaintFont * perView;
                Canvas.SetTop(name, t - FaintFont * perView * 1.4);
            }
        }
    }

    void Add(UIElement e, double left, double top)
    {
        Canvas.SetLeft(e, left);
        Canvas.SetTop(e, top);
        _faint.Children.Add(e);
    }
}
