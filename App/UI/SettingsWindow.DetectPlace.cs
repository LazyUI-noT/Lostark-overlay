using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>화면·인식 → 감지: placing a box. The same edit as 위치 조정's layer (<see cref="FitAdjust"/>, the engine's preview session,
/// App.Adjust.cs applies and saves), done on the page's picture: a box <see cref="DetectPlace.PartOf"/> knows is dragged, its corner
/// dot sizes it, 되돌리기 / 적용 sit with the other buttons. It is green when it reads: the engine's live reads while Live runs, else
/// one read of the picture shown (<see cref="PictureReads"/>). A box nothing moves has no dot and no move cursor.</summary>
public partial class SettingsWindow
{
    static readonly Brush GreenFill = new SolidColorBrush(Color.FromArgb(60, 0x3F, 0xB9, 0x50));
    const double HandleTolerance = 10;   // screen px (as the screen is shown) around a size dot that take it

    /// <summary>The player asks for a placing session (the engine's preview).</summary>
    public event Action? PlaceRequested;
    /// <summary>A new fit and 버프 크기 to preview, for session gen.</summary>
    public event Action<int, Fit, int>? PlaceEdited;
    /// <summary>적용: the game area, the fit, the game's 버프 크기, and whether it is 테스트 모드's.</summary>
    public event Action<Viewport, Fit, int, bool>? PlaceApplied;
    /// <summary>Ended without 적용 (그만두기, leaving the page): back to the saved fit.</summary>
    public event Action? PlaceEnded;

    FitAdjust? _place;
    int _placeGen, _placeBuffCount, _placeBossSize;
    bool _placeWanted, _placeTest;
    AdjustReads? _placeLive;
    string _placeNote = "";
    PixelRect _detectShown;   // the screen as drawn now (the drag's origin)
    (FitAdjust Start, AdjustPart Part, bool Corner, Point At)? _placeDrag;
    readonly Dictionary<string, bool?> _placeGreen = [];

    void HookPlace()
    {
        _detectScreen.MouseLeftButtonDown += PlacePress;
        _detectScreen.MouseMove += PlaceDrag;
        _detectScreen.MouseLeftButtonUp += (_, _) => _detectScreen.ReleaseMouseCapture();
        _detectScreen.LostMouseCapture += (_, _) => _placeDrag = null;   // also a dialog or Alt+Tab mid-drag
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
                .Select(p => PictureReads.Of(part, p.Picture, p.At, a.Vp, a.Current, a.BuffSize, _placeBuffCount, _placeBossSize)).FirstOrDefault(r => r is not null) ?? _placeLive?.Of(part);
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
        if (_place is not { } a || e.OriginalSource is Rectangle { Tag: string over } && DetectPlace.PartOf(over) is null) return;   // a box nothing moves
        var at = e.GetPosition(_detectScreen);
        if (a.PartAt(_detectShown.X + at.X, _detectShown.Y + at.Y, Px(HandleTolerance)) is not { } hit) return;
        _detectPicked = DetectPlace.Movable[(int)hit.Part];
        _placeDrag = (a, hit.Part, hit.Corner, at);
        _detectScreen.CaptureMouse();
        DrawDetect();
    }

    void PlaceDrag(object sender, MouseEventArgs e)
    {
        if (_placeDrag is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(_detectScreen);
        double dx = at.X - d.At.X, dy = at.Y - d.At.Y;   // the picture is 1 px per screen px; the whole distance from the press, so rounding never drifts
        PlaceChange(d.Corner ? d.Start.DragCorner(d.Part, dx) : d.Start.Move(d.Part, dx, dy));
    }

    void PlaceChange(FitAdjust next)
    {
        _place = next;
        RefreshGreen();
        PlaceEdited?.Invoke(_placeGen, next.Current, next.BuffSize);
        DrawDetect();
    }

    void PlaceTools(string id)
    {
        if (_place is { } a)
        {
            Tool("되돌리기", () => PlaceChange(a.Reverted()), a.Changed);
            Tool("적용", ApplyPlace);
            Tool("그만두기", EndPlace);
        }
        else if (_placeWanted) Tool("그만두기", EndPlace);
        else if (DetectPlace.PartOf(id) is not null) Tool("자리 옮기기", () => { _placeWanted = true; _placeNote = ""; PlaceRequested?.Invoke(); });
    }

    string PlaceLine()
    {
        if (_placeNote != "") return "\n" + _placeNote;
        if (_place is not null)
            return "\n옮기는 중입니다. 칸을 끌어 옮기고, 칸 모서리의 흰 점을 끌어 크기를 바꿉니다. 초록 칸은 지금 읽히는 칸입니다. 아무것도 안 보이는 칸은 틀린 게 아니라 색이 없을 뿐입니다. "
                + "버프가 사라졌다면 Live를 켜고 일시정지로 그림을 멈춘 뒤 맞추세요. 적용하면 저장하고, 그만두기는 저장하지 않고 원래 자리로 돌아갑니다.";
        if (_placeWanted) return "\n게임 화면을 찾는 중입니다. 위치 조정 화면이 열려 있으면 먼저 닫아 주세요.";
        return _detectPicked is not { } id ? ""
            : DetectPlace.PartOf(id) is null ? "\n이 칸은 옮길 수 없습니다. 자리는 해상도, HUD 크기, 스킬창 위치로 정해집니다."
            : "\n자리 옮기기를 누르면 이 칸을 화면에서 끌어 옮기고 크기를 바꿀 수 있습니다.";
    }

    /// <summary>The engine's preview session began (or its game area changed): the edit starts on <paramref name="fit"/>.</summary>
    public void PlaceStarted(int gen, Viewport vp, Fit fit, int buffSize, bool test, int buffCount, int bossSize)
    {
        if (!_placeWanted) return;
        (_placeGen, _placeTest, _placeBuffCount, _placeBossSize, _placeLive, _placeDrag) = (gen, test, buffCount, bossSize, null, null);
        _place = FitAdjust.Begin(vp, buffSize, fit, buffCount, bossSize);
        if (_place.Changed) PlaceEdited?.Invoke(gen, _place.Current, _place.BuffSize);   // the start was outside the game area: clamped
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
        (_place, _placeWanted, _placeDrag, _placeLive) = (null, false, null, null);
        if (note is not null) _placeNote = note;
        RefreshGreen();
        if (_secDetect.IsVisible) DrawDetect();
    }

    void ApplyPlace()
    {
        if (_place is not { } a) return;
        (_place, _placeWanted, _placeDrag) = (null, false, null);
        _placeNote = "적용하는 중입니다.";
        RefreshGreen();
        PlaceApplied?.Invoke(a.Vp, a.Current, a.BuffSize, _placeTest);
    }

    void EndPlace()
    {
        bool was = _placeWanted;
        (_place, _placeWanted, _placeDrag, _placeLive) = (null, false, null, null);
        RefreshGreen();
        if (was) PlaceEnded?.Invoke();
    }
}
