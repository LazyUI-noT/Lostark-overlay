using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>화면 인식 설정: placing a box. The same edit as 위치 조정's layer (<see cref="FitAdjust"/>, the engine's preview session,
/// App.Adjust.cs applies and saves), done on the page's picture: pressing a box <see cref="DetectPlace.PartOf"/> knows starts the
/// session and drags it, its corner dot sizes it, 되돌리기 (<see cref="PlaceHistory"/>, also Ctrl+Z) / 처음으로 / 적용 sit with the other
/// buttons. It is green when it reads: the engine's live reads while Live runs, else one read of the picture shown
/// (<see cref="PictureReads"/>). A box nothing moves has no dot and no move cursor.</summary>
public partial class SettingsWindow
{
    static readonly Brush GreenFill = new SolidColorBrush(Color.FromArgb(60, 0x3F, 0xB9, 0x50));
    const double HandleTolerance = 10;   // screen px (as the screen is shown) around a size dot that take it

    /// <summary>The player asks for a placing session (the engine's preview).</summary>
    public event Action? PlaceRequested;
    /// <summary>The edit to preview, for session gen.</summary>
    public event Action<int, FitAdjust>? PlaceEdited;
    /// <summary>적용: the edit, and whether it is 테스트 모드's.</summary>
    public event Action<FitAdjust, bool>? PlaceApplied;
    /// <summary>Ended without 적용 (그만두기, leaving the page): back to the saved fit.</summary>
    public event Action? PlaceEnded;

    FitAdjust? _place;
    int _placeGen, _placeBuffCount, _placeBossSize;
    bool _placeWanted, _placeTest;
    AdjustReads? _placeLive;
    string _placeNote = "";
    PixelRect _detectShown;   // the screen as drawn now
    (FitAdjust Start, AdjustPart Part, bool Corner, Point At)? _placeDrag;
    (AdjustPart Part, Point At)? _placePress;   // held on a box before its session has started: the drag begins from here
    PlaceHistory _placeHistory = PlaceHistory.Empty;
    readonly Dictionary<string, bool?> _placeGreen = [];

    void HookPlace()
    {
        _detectScreen.MouseLeftButtonDown += PlacePress;
        _detectScreen.MouseMove += PlaceDrag;
        _detectScreen.MouseLeftButtonUp += (_, _) => _detectScreen.ReleaseMouseCapture();
        _detectScreen.LostMouseCapture += (_, _) => PlaceLetGo();   // also a dialog or Alt+Tab mid-drag
        (_detectScreen.Focusable, _detectScreen.FocusVisualStyle) = (true, null);   // a press takes the keys: the picked box's frame shows it
        _detectScreen.PreviewKeyDown += PlaceKey;
    }

    /// <summary>The picked box after a press: arrows move it 1 px (Shift 10), Ctrl+arrows size it in its own shape (→ ↑ bigger).
    /// A press, held as long as it repeats, is one step 되돌리기 goes back over.</summary>
    void PlaceKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        (int dx, int dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
        if ((dx, dy) == (0, 0) || _placeWindow is null || _placeDrag is not null || _place is not { } a || _detectPicked is not { } id || DetectPlace.PartOf(id) is not { } part) return;
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var next = a.Arrow(part, dx * step, dy * step, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        e.Handled = true;   // also at a wall: the arrow never moves the page's focus or scroll
        if (next == a) return;
        if (!e.IsRepeat) _placeHistory = _placeHistory.After(a, next);   // a held key: one step, back to before the press
        PlaceChange(next);
    }

    /// <summary>An example of a well-placed box per part, cut from 1080p footage with the same lines drawn (App/Assets/examples).</summary>
    static readonly Dictionary<AdjustPart, (string File, string Text)> PlaceExamples = new()
    {
        [AdjustPart.스킬창] = ("place-skillbar.png", "각 스킬 아이콘이 가는 네모 안에 꼭 맞게 들어가도록 맞춥니다."),
        [AdjustPart.버프] = ("place-buffs.png", "아이콘은 세로선 사이 칸마다 하나씩, 가로선 위에 오고 남은 시간 글자는 가로선 아래에 오게 맞춥니다."),
        [AdjustPart.디버프] = ("place-debuffs.png", "버프 줄과 같습니다: 아이콘은 칸마다 하나씩 가로선 위에, 남은 시간은 그 아래에 옵니다."),
        [AdjustPart.보스] = ("place-boss.png", "세로선을 초록 테두리 아이콘(보스 버프)과 빨간 테두리 아이콘(보스 디버프) 사이 빈틈에 맞춥니다. \"모든 면역 / 관통 면역\" 글자가 보이면 그 / 자리입니다."),
        [AdjustPart.전분] = ("place-jeonbun.png", "전투 분석기 창 전체(시간 줄부터 숫자 줄까지)가 칸 안에 꼭 맞게 들어오도록 맞춥니다."),
    };
    readonly StackPanel _placeExample = new() { Margin = new(0, Tokens.Space.Gap, 0, 0) };
    AdjustPart? _placeExampleShown;

    /// <summary>The picked box's example while it can be placed.</summary>
    void ShowPlaceExample()
    {
        AdjustPart? part = _detectPicked is { } id ? DetectPlace.PartOf(id) : null;
        if (part == _placeExampleShown) return;
        _placeExampleShown = part;
        _placeExample.Children.Clear();
        if (part is not { } p || !PlaceExamples.TryGetValue(p, out var ex) || BossBarExample.Load(ex.File) is not { } picture) return;
        _placeExample.Children.Add(new TextBlock { Text = "예시: " + ex.Text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, Tokens.Space.Tight) });
        _placeExample.Children.Add(new System.Windows.Controls.Image { Source = picture, MaxWidth = 560, MaxHeight = 160, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
    }

    /// <summary>Where the icons go inside a placed box (<see cref="FitAdjust.Guides"/>), thin in its colour.</summary>
    void AddGuides(AdjustPart part, Color colour, PixelRect screen)
    {
        if (_place is not { } a) return;
        var brush = new SolidColorBrush(Color.FromArgb(0xB0, colour.R, colour.G, colour.B));
        foreach (var (x1, y1, x2, y2) in a.Guides(part))
            _detectScreen.Children.Add(new Line { X1 = x1 - screen.X, Y1 = y1 - screen.Y, X2 = x2 - screen.X, Y2 = y2 - screen.Y, Stroke = brush, StrokeThickness = Px(1), IsHitTestVisible = false });
    }

    /// <summary>A box's name and map colour; the debuff row has no map box.</summary>
    static (string Title, string Color)? LookOf(string id) =>
        id == DetectPlace.DebuffRow ? (DetectPlace.DebuffRowTitle, "#8A8F98")
        : FeatureMap.Boxes.FirstOrDefault(b => b.Id == id) is { } box ? (box.Title, box.Color) : null;

    /// <summary>The areas drawn: the engine's, and while placing the four boxes at the edit's own rects (instant, as the layer shows them).
    /// A box not on the saved picture behind them stays, drawn as an outline only (<see cref="OffPicture"/>).</summary>
    IReadOnlyDictionary<string, PixelRect> ShownAreas()
    {
        if (_place is null) return _detectAreas;
        var shown = _detectAreas.ToDictionary(k => k.Key, k => k.Value);
        foreach (var id in DetectPlace.Movable) shown[id] = _place.RectOf(DetectPlace.PartOf(id)!.Value);
        return shown;
    }

    /// <summary>A saved picture is shown and box <paramref name="id"/> isn't on that screen (캐릭터 선택 has no boss row).</summary>
    bool OffPicture(string id) => _readingShown is { } picture && !ReadingShots.BoxesOf(picture.Kind).Contains(id);

    bool? Green(string id) => _placeGreen.GetValueOrDefault(id);

    /// <summary>Green per moved box: Live running → the engine's reads of the game now; else the picture shown (the paused Live, the
    /// stored moment, 화면 찍기) read once at the edit's fit; null where the picture doesn't hold the box's area.</summary>
    void RefreshGreen()
    {
        _placeGreen.Clear();
        if (_place is not { } a) return;
        foreach (var id in DetectPlace.Movable)
        {
            var part = DetectPlace.PartOf(id)!.Value;
            bool liveNow = _detectLiveId == id && !_detectPaused;
            _placeGreen[id] = liveNow ? _placeLive?.Of(part) : StillPictures(id)
                .Select(p => PictureReads.Of(part, p.Picture, p.At, a.Vp, a.Current, a.BuffSize, _placeBuffCount, _placeBossSize, a.Panel)).FirstOrDefault(r => r is not null) ?? _placeLive?.Of(part);
        }
    }

    /// <summary>The pictures behind box <paramref name="id"/>, the one drawn on top first.</summary>
    IEnumerable<(Core.Domain.Image Picture, PixelRect At)> StillPictures(string id)
    {
        if (_detectLiveId == id && _detectLive is { } live) yield return (live.Picture, live.Area);
        else if (_detect.GetValueOrDefault(id) is { Mark: not DetectMark.Waiting } && _detectPictures.GetValueOrDefault(id) is { } kept) yield return (kept.Picture, kept.Area);
        if (_detectShotImage is { } shot && _detectShot is { } at && !OffPicture(id)) yield return (shot, at.Area);
    }

    /// <summary>The screen as shown against the screen as it is: how much a screen pixel is shrunk.</summary>
    double ShownScale => _detectScreen.Parent is Viewbox v && v.ActualHeight > 0 && _detectScreen.Height > 0 ? v.ActualHeight / _detectScreen.Height : 0.3;

    /// <summary>A size in pixels on screen as it should be in the picture to look the same at any zoom.</summary>
    double Px(double onScreen) => onScreen / Math.Max(ShownScale, 0.05);

    void AddHandle(AdjustPart part, Color colour, PixelRect screen)
    {
        var (x, y) = _place!.Handle(part);
        double size = Px(9);
        var dot = new Rectangle { Width = size, Height = size, Fill = Brushes.White, Stroke = new SolidColorBrush(colour), StrokeThickness = Px(1.5), IsHitTestVisible = false };
        Canvas.SetLeft(dot, x - screen.X - size / 2);
        Canvas.SetTop(dot, y - screen.Y - size / 2);
        _detectScreen.Children.Add(dot);
    }

    void PlacePress(object sender, MouseButtonEventArgs e)
    {
        if (_placeWindow is null) return;   // the page only picks: placing is in the zoom window
        if (e.OriginalSource is Rectangle { Tag: string over } && DetectPlace.PartOf(over) is null) return;   // a box nothing moves
        var at = e.GetPosition(_detectScreen);
        if (at.Y >= _detectShown.Height) return;   // the 예시 panel under the capture only shows: a press there picked another box at that height, and the panel went with it
        if (_place is not { } a)
        {
            if (e.OriginalSource is Rectangle { Tag: string id } && DetectPlace.PartOf(id) is { } part) PlacePressBeforeSession(id, part, OnHost(e));
            return;
        }
        if (a.PartAt(_detectShown.X + at.X, _detectShown.Y + at.Y, Px(HandleTolerance)) is not { } hit) return;
        (_detectPicked, _placeNote, _placeDrag) = (DetectPlace.Movable[(int)hit.Part], "", (a, hit.Part, hit.Corner, OnHost(e)));
        _detectScreen.Focus();
        _detectScreen.CaptureMouse();
        DrawDetect();
    }

    /// <summary>The press that starts a session. The mouse stays held on the screen, so what it moves meanwhile counts once the
    /// session starts (<see cref="PlaceStarted"/>): a drag's distance is taken from the press.</summary>
    void PlacePressBeforeSession(string id, AdjustPart part, Point at)
    {
        (_detectPicked, _placeNote, _placePress) = (id, "", (part, at));
        _detectScreen.Focus();
        _detectScreen.CaptureMouse();
        if (!_placeWanted)
        {
            _placeWanted = true;
            PlaceRequested?.Invoke();
        }
        DrawDetect();
    }

    /// <summary>The mouse on the screen's Viewbox (the zoom window's, framed by <c>_placeFrame</c>): measured there rather than on the
    /// screen itself, so a redraw of the screen under the mouse never counts as the mouse moving.</summary>
    Point OnHost(MouseEventArgs e) => e.GetPosition((IInputElement)_detectScreen.Parent);

    /// <summary>The mouse let go or taken away: a finished drag is one step to go back over.</summary>
    void PlaceLetGo()
    {
        _placePress = null;
        if (_placeDrag is not { } d) return;
        _placeDrag = null;
        if (_place is not { } now) return;
        _placeHistory = _placeHistory.After(d.Start, now);
        DrawDetect();
    }

    void PlaceDrag(object sender, MouseEventArgs e)
    {
        if (_placeDrag is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
        var at = OnHost(e);
        double scale = Math.Max(ShownScale, 0.05);
        double dx = (at.X - d.At.X) / scale, dy = (at.Y - d.At.Y) / scale;   // in screen px; the whole distance from the press, so rounding never drifts
        PlaceChange(d.Corner ? d.Start.DragCorner(d.Part, dx) : d.Start.Move(d.Part, dx, dy));
    }

    void PlaceChange(FitAdjust next)
    {
        _place = next with { BuffFollowsBar = _s.BuffFollowsBar };   // a step 되돌리기 brings back keeps today's check
        RefreshGreen();
        PlaceEdited?.Invoke(_placeGen, next);
        DrawDetect();
    }

    /// <summary>되돌리기 and Ctrl+Z: one step back. False with no session wanted, where Ctrl+Z is 설정's own undo.</summary>
    bool PlaceBack()
    {
        if (!_placeWanted) return false;
        if (_place is not null && _placeDrag is null && _placeHistory.Back() is { } back)
        {
            _placeHistory = back.History;
            PlaceChange(back.Step);
        }
        return true;
    }

    /// <summary>처음으로: back to where the session opened, itself a step 되돌리기 can go back over.</summary>
    void PlaceReset()
    {
        if (_place is not { } a) return;
        var start = a.Reverted();
        _placeHistory = _placeHistory.After(a, start);
        PlaceChange(start);
    }

    string PlaceLine()
    {
        if (_placeNote != "") return "\n" + _placeNote;
        if (_place is { } a)
            return (a.Changed ? "\n아직 저장되지 않았습니다. 적용을 누르지 않고 이 화면을 나가면 옮긴 자리는 사라집니다." : "")
                // Only what the buttons don't say (critique, 2026-11-17): 적용·취소·되돌리기·처음으로 name themselves.
                + "\n끌어서 옮기고 모서리의 흰 점으로 크기를 바꿉니다 · 화살표 1px (Shift 10px) · Ctrl+화살표 크기 (→↑ 크게) · Enter 적용 · Esc 취소"
                + "\n칸 안의 가는 선에 아이콘이 들어가게 맞추세요. 초록 칸은 지금 읽히는 칸이고, 아무것도 안 보이는 칸은 틀린 게 아닙니다."
                + "\n전분은 게임에서 열어 둔 채로, 사라지는 버프는 Live를 일시정지해 멈춘 뒤 맞추세요.";
        if (_placeWanted) return "\n게임 화면을 찾는 중입니다. 수동 위치 조정 화면이 열려 있으면 먼저 닫으세요.";
        return _detectPicked is not { } id ? ""
            : DetectPlace.PartOf(id) is null ? "\n이 칸은 옮길 수 없습니다. 자리는 해상도와 HUD 크기로 정해집니다."
            : "\n자리 맞추기(Enter)를 누르면 크게 보며 이 칸을 옮깁니다.";
    }

    /// <summary>The engine's preview session began (or its game area changed): the edit starts on <paramref name="fit"/>.</summary>
    public void PlaceStarted(int gen, Viewport vp, Fit fit, int buffSize, bool test, int buffCount, int bossSize, PixelRect? panel,
        IReadOnlyDictionary<SlotId, PixelRect>? aden = null)
    {
        if (!_placeWanted) return;
        (_placeGen, _placeTest, _placeBuffCount, _placeBossSize, _placeLive, _placeDrag) = (gen, test, buffCount, bossSize, null, null);
        _place = FitAdjust.Begin(vp, buffSize, fit, buffCount, bossSize, panel) with { BuffFollowsBar = _s.BuffFollowsBar, AdenRects = aden };
        (_placeHistory, _placeNote) = (PlaceHistory.Empty, "");   // a new session: its steps are for another fit or game area; an earlier 적용's note would hide the not-saved line
        if (_placePress is { } held) (_placeDrag, _placePress) = ((_place, held.Part, false, held.At), null);
        PlaceEdited?.Invoke(gen, _place);   // the 전분 box to look at, and a start outside the game area clamped
        RefreshGreen();
        DrawDetect();
    }

    /// <summary>The session's read states, ~5 a second.</summary>
    public void PlaceReads(int gen, AdjustReads reads)
    {
        if (gen != _placeGen || _place is null || reads == _placeLive) return;
        _placeLive = reads;
        RefreshGreen();
        if (_secDetect.IsVisible) DrawDetect();
    }

    /// <summary>적용 was kept: what was saved.</summary>
    public void PlaceCommitted(string text)
    {
        _placeNote = text;
        if (_secDetect.IsVisible) DrawDetect();
    }

    /// <summary>App ended the session (the game is gone, 위치 조정 took over): no event back.</summary>
    public void PlaceStopped(string? note = null)
    {
        (_place, _placeWanted, _placeDrag, _placePress, _placeLive) = (null, false, null, null, null);
        if (note is not null) _placeNote = note;
        RefreshGreen();
        if (_secDetect.IsVisible) DrawDetect();
    }

    void ApplyPlace()
    {
        if (_place is not { } a) return;
        (_place, _placeWanted, _placeDrag, _placePress) = (null, false, null, null);
        _placeNote = "적용하는 중입니다.";
        RefreshGreen();
        PlaceApplied?.Invoke(a, _placeTest);
    }

    void EndPlace()
    {
        bool was = _placeWanted;
        (_place, _placeWanted, _placeDrag, _placePress, _placeLive) = (null, false, null, null, null);
        RefreshGreen();
        if (was) PlaceEnded?.Invoke();
    }
}
