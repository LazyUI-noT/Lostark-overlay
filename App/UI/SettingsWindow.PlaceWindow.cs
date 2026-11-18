using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>읽는 자리 → 자리 맞추기 (reading-place.plan.md C): the picked box in <see cref="PlaceWindow"/>. The page's screen canvas
/// moves into the window, framed on the box, while it is open; a box <see cref="DetectPlace.PartOf"/> knows starts the placing
/// session there. The page itself only picks.</summary>
public partial class SettingsWindow
{
    PlaceWindow? _placeWindow;
    PixelRect? _placeFrame;   // the window's frame: fixed while open, so a drag never rescales the view
    Viewbox? _placeHome;      // the page's host of the screen canvas, to put it back
    bool _placeSample;        // 예시 보기: the sample picture in place of the capture

    /// <summary>The picked box in the zoom window (Enter, a double-click, 자리 맞추기).</summary>
    void OpenPlace()
    {
        if (_placeWindow is not null) { _placeWindow.Activate(); return; }
        if (_detectPicked is not { } id || !ShownAreas().TryGetValue(id, out var area) || LookOf(id) is not { } look) return;
        var part = DetectPlace.PartOf(id);
        bool movable = part is not null, hasSample = part is { } p && PlaceSample.All.ContainsKey(p);
        var around = DetectBoard.Around(area, _detectView) ?? area;
        _placeFrame = DetectBoard.Around(around, _detectView) ?? around;   // twice the margin: room to move the box
        _placeHome = _detectScreen.Parent as Viewbox;
        if (_placeHome is not null) _placeHome.Child = null;
        try { _placeWindow = new PlaceWindow(this, _s.Theme, _detectScreen, $"자리 맞추기 · {look.Title}", movable, hasSample, FollowCheck(id)); }
        catch
        {
            if (_placeHome is not null && _detectScreen.Parent is null) _placeHome.Child = _detectScreen;   // the page keeps its screen
            (_placeFrame, _placeHome) = (null, null);
            throw;
        }
        _placeWindow.ApplyRequested += () =>
        {
            if (_place is null) { _placeNote = "아직 게임 화면을 찾는 중입니다. 잠시 뒤에 적용하세요."; DrawDetect(); return; }   // closing now would drop the edit
            ApplyPlace();
            _placeWindow?.Close();
        };
        _placeWindow.BackRequested += () => PlaceBack();
        _placeWindow.ResetRequested += PlaceReset;
        _placeWindow.SampleToggled += on => { _placeSample = on; DrawDetect(); };
        _placeWindow.Closed += (_, _) => ClosePlace();
        if (movable && !_placeWanted)
        {
            _placeWanted = true;
            PlaceRequested?.Invoke();
        }
        DrawDetect();
        _placeWindow.Show();
    }

    /// <summary>스킬창과 함께 움직임, only on the 스킬창 and 버프 boxes: off, the buff row keeps its spot while the bar moves.</summary>
    CheckBox? FollowCheck(string id) => DetectPlace.PartOf(id) is AdjustPart.스킬창 or AdjustPart.버프
        ? UiBuild.Check("스킬창과 함께 움직임", _s.BuffFollowsBar, v =>
            {
                _s.BuffFollowsBar = v;
                if (_place is { } a) _place = a with { BuffFollowsBar = v };
            }, Changed, "끄면 스킬창을 옮기거나 크기를 바꿔도 버프 줄은 그 자리에 남습니다. 버프 줄만 어긋났을 때 끄세요.")
        : null;

    /// <summary>The window closed (취소, Esc, 적용, or 설정 leaving the page): an unapplied session ends, the canvas goes home.</summary>
    void ClosePlace()
    {
        if (_placeWindow is null) return;
        (_placeWindow, _placeFrame, _placeSample) = (null, null, false);
        if (_placeWanted) EndPlace();
        Detach(_detectScreen);   // the closed window's Viewbox still holds it: "이미 다른 요소의 논리 자식" otherwise, and the page loses its screen
        if (_placeHome is not null) _placeHome.Child = _detectScreen;
        _placeHome = null;
        DrawDetect();
    }

    /// <summary>On the page: a double-click or Enter on the picked box opens it.</summary>
    void HookOpenPlace()
    {
        _detectScreen.PreviewMouseLeftButtonDown += (_, e) => { if (_placeWindow is null && e.ClickCount == 2 && _detectPicked is not null) { OpenPlace(); e.Handled = true; } };
        _detectScreen.PreviewKeyDown += (_, e) => { if (_placeWindow is null && e.Key == Key.Enter && _detectPicked is not null) { OpenPlace(); e.Handled = true; } };
    }

    /// <summary>The window's line and buttons follow the session.</summary>
    void PlaceWindowState() =>
        _placeWindow?.SetState(PlaceLine().TrimStart('\n'), _placeHistory.CanGoBack, _place is { Changed: true });

    /// <summary>예시 보기 on and the picked box has a sample: how far below the capture the sample panel starts, else 0. The panel
    /// sits under the capture, not over it (user, 2026-11-17): the capture stays as it is. Boxes that reach past the capture's
    /// edge are cut there (<see cref="DrawPlaceSample"/>), not waited for: 단심 or 캐릭터 left a large gap (user, 2026-11-18).</summary>
    double PlaceSampleOffset(PixelRect screen) =>
        _placeSample && _placeWindow is not null && _place is not null && _detectPicked is { } id && DetectPlace.PartOf(id) is { } part
            && PlaceSample.All.ContainsKey(part) ? screen.Height + SampleGap(screen) : 0;

    static double SampleGap(PixelRect screen) => Math.Max(16, screen.Height / 10.0);   // clears a box's corner handle

    /// <summary>The sample panel under the capture: the same frame of the screen with the 예시 picture where the game would draw it
    /// around the box as placed now, and that box's frame and guides copied on it, so the two can be compared line for line.</summary>
    void DrawPlaceSample(PixelRect screen, double top)
    {
        if (top <= 0 || _place is not { } a || _detectPicked is not { } id || DetectPlace.PartOf(id) is not { } part || LookOf(id) is not { } look
            || !ShownAreas().TryGetValue(id, out var area) || !PlaceSample.All.TryGetValue(part, out var sample)
            || BossBarExample.Load(sample.File) is not { } picture) return;
        int first = _detectScreen.Children.Count;   // drawn by the page's own helpers, then moved into the clipped panel
        PlacePicture(picture, sample.At(a, part), screen);
        var colour = (Color)ColorConverter.ConvertFromString(look.Color);
        var frame = new System.Windows.Shapes.Rectangle { Width = Math.Max(4, area.Width), Height = Math.Max(4, area.Height), Stroke = new SolidColorBrush(colour), StrokeThickness = Math.Max(2, screen.Width / 640.0) };
        Canvas.SetLeft(frame, area.X - screen.X);
        Canvas.SetTop(frame, area.Y - screen.Y);
        _detectScreen.Children.Add(frame);
        AddGuides(part, colour, screen);
        var drawn = _detectScreen.Children.Cast<UIElement>().Skip(first).ToList();
        foreach (var e in drawn) _detectScreen.Children.Remove(e);
        var panel = new Canvas { Width = screen.Width, Height = screen.Height, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F)) };
        foreach (var e in drawn) panel.Children.Add(e);
        panel.Children.Add(SampleMark(screen));
        Canvas.SetTop(panel, top);
        // The capture ends at its edge: what runs past it (a tall box) is cut by a band of the page's colour above the panel.
        var cut = new System.Windows.Shapes.Rectangle { Width = screen.Width, Height = top - screen.Height, Fill = panel.Background };   // band and panel take the clicks: a box running under them can't be picked there
        Canvas.SetTop(cut, screen.Height);
        _detectScreen.Children.Add(cut);
        _detectScreen.Children.Add(panel);
    }

    const double SampleMarkShare = 0.9, SampleMarkOpacity = 0.35;

    /// <summary>예시 across the whole panel as a faint white outline, no fill (user, 2026-11-18, option A): plain to see, and the
    /// icons to line up show through it. A small solid label was easy to miss.</summary>
    static System.Windows.Shapes.Path SampleMark(PixelRect screen)
    {
        var face = new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var text = new FormattedText("예시", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 100, Brushes.White, 1.0);
        var mark = new System.Windows.Shapes.Path
        {
            Data = text.BuildGeometry(new Point(0, 0)), Stretch = Stretch.Uniform, Stroke = Brushes.White, Opacity = SampleMarkOpacity,
            StrokeThickness = Math.Max(2, screen.Width / 300.0), Width = screen.Width * SampleMarkShare, Height = screen.Height * SampleMarkShare,
        };
        Canvas.SetLeft(mark, screen.Width * (1 - SampleMarkShare) / 2);
        Canvas.SetTop(mark, screen.Height * (1 - SampleMarkShare) / 2);
        return mark;
    }
}
