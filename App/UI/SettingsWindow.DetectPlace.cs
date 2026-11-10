using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>화면·인식 → 감지: placing a box. The same edit as 위치 조정's layer (<see cref="FitAdjust"/>, the engine's preview session,
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
    }

    /// <summary>A box's name and map colour; the debuff row has no map box.</summary>
    static (string Title, string Color)? LookOf(string id) =>
        id == DetectPlace.DebuffRow ? (DetectPlace.DebuffRowTitle, "#8A8F98")
        : FeatureMap.Boxes.FirstOrDefault(b => b.Id == id) is { } box ? (box.Title, box.Color) : null;

    /// <summary>The areas drawn: the engine's, and while placing the four boxes at the edit's own rects (instant, as the layer shows them).</summary>
    IReadOnlyDictionary<string, PixelRect> ShownAreas()
    {
        if (_place is not { } a) return _detectAreas;
        var shown = _detectAreas.ToDictionary(k => k.Key, k => k.Value);
        foreach (var id in DetectPlace.Movable) shown[id] = a.RectOf(DetectPlace.PartOf(id)!.Value);
        return shown;
    }

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
        if (_detectShotImage is { } shot && _detectShot is { } at) yield return (shot, at.Area);
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
        if (e.OriginalSource is Rectangle { Tag: string over } && DetectPlace.PartOf(over) is null) return;   // a box nothing moves
        var at = e.GetPosition(_detectScreen);
        if (_place is not { } a)
        {
            if (e.OriginalSource is Rectangle { Tag: string id } && DetectPlace.PartOf(id) is { } part) PlacePressBeforeSession(id, part, OnHost(e));
            return;
        }
        if (a.PartAt(_detectShown.X + at.X, _detectShown.Y + at.Y, Px(HandleTolerance)) is not { } hit) return;
        (_detectPicked, _placeNote, _placeDrag) = (DetectPlace.Movable[(int)hit.Part], "", (a, hit.Part, hit.Corner, OnHost(e)));
        _detectScreen.CaptureMouse();
        DrawDetect();
    }

    /// <summary>The press that starts a session. The mouse stays held on the screen, so what it moves meanwhile counts once the
    /// session starts (<see cref="PlaceStarted"/>): a drag's distance is taken from the press.</summary>
    void PlacePressBeforeSession(string id, AdjustPart part, Point at)
    {
        (_detectPicked, _placeNote, _placePress) = (id, "", (part, at));
        _detectScreen.CaptureMouse();
        if (!_placeWanted)
        {
            _placeWanted = true;
            PlaceRequested?.Invoke();
        }
        DrawDetect();
    }

    /// <summary>The mouse on the screen's Viewbox, which stays put: 확대 reframes and rescales the screen inside it as the picked box
    /// changes or moves, so a drag measured on the screen itself would count that as the mouse moving.</summary>
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
        _place = next;
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

    void PlaceTools()
    {
        if (_place is { } a)
        {
            Tool("되돌리기", () => PlaceBack(), _placeHistory.CanGoBack);
            Tool("처음으로", PlaceReset, a.Changed);
            UiBuild.MarkUnapplied(Tool("적용", ApplyPlace), a.Changed);
            Tool("그만두기", EndPlace);
        }
        else if (_placeWanted) Tool("그만두기", EndPlace);
    }

    string PlaceLine()
    {
        if (_placeNote != "") return "\n" + _placeNote;
        if (_place is { } a)
            return (a.Changed ? "\n아직 저장되지 않았습니다. 적용을 누르지 않고 이 화면을 나가면 옮긴 자리는 사라집니다." : "")
                + "\n옮기는 중입니다. 칸을 끌어 옮기고, 칸 모서리의 흰 점을 끌어 크기를 바꿉니다. 초록 칸은 지금 읽히는 칸입니다. 전분은 게임에서 전투 분석기를 열어 둔 채로 맞추세요. 아무것도 안 보이는 칸은 틀린 게 아니라 색이 없을 뿐입니다. "
                + $"버프가 사라졌다면 Live를 켜고 일시정지로 그림을 멈춘 뒤 맞추세요. 되돌리기(Ctrl+Z)는 {PlaceHistory.MaxSteps}번까지 한 번씩 되돌리고, 처음으로는 처음 자리로 돌아갑니다. 적용하면 저장하고, 그만두기는 저장하지 않고 원래 자리로 돌아갑니다.";
        if (_placeWanted) return "\n게임 화면을 찾는 중입니다. 위치 조정 화면이 열려 있으면 먼저 닫아 주세요.";
        return _detectPicked is not { } id ? ""
            : DetectPlace.PartOf(id) is null ? "\n이 칸은 옮길 수 없습니다. 자리는 해상도, HUD 크기, 스킬창 위치로 정해집니다."
            : "\n이 칸은 바로 끌어 옮길 수 있습니다. 한 번 누르면 크기를 바꾸는 흰 점이 나옵니다.";
    }

    /// <summary>The engine's preview session began (or its game area changed): the edit starts on <paramref name="fit"/>.</summary>
    public void PlaceStarted(int gen, Viewport vp, Fit fit, int buffSize, bool test, int buffCount, int bossSize, PixelRect? panel)
    {
        if (!_placeWanted) return;
        (_placeGen, _placeTest, _placeBuffCount, _placeBossSize, _placeLive, _placeDrag) = (gen, test, buffCount, bossSize, null, null);
        _place = FitAdjust.Begin(vp, buffSize, fit, buffCount, bossSize, panel);
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
