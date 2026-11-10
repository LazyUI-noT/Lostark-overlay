using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;
using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>화면·인식 → 감지 (Phase 5): the game screen at its proportions with one thin box per reader where it reads, in its
/// map colour with its 감지 중 mark. A ✓ or ! box shows the engine's picture of the moment it turned; a ? box is dashed with no
/// picture. A click picks a box: under the screen its Live (that box only, 2/s), 일시정지 (keeps the picture, captures
/// nothing), 확대 (the screen zoomed onto it) and its result. 화면 찍기 puts the whole screen behind (kept in memory, never
/// saved). Leaving the page turns Live off. A box 위치 조정 moves can be placed here too (SettingsWindow.DetectPlace.cs), and the
/// picked box's rows of the old 진단 table sit under the screen.</summary>
public partial class SettingsWindow
{
    const string SecDetect = "감지";
    readonly CardStack _secDetect = new();
    readonly Canvas _detectScreen = new() { ClipToBounds = true };
    readonly WrapPanel _detectTools = new() { Margin = new(0, 8, 0, 0) };
    readonly TextBlock _detectDetail = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    IReadOnlyDictionary<string, PixelRect> _detectAreas = new Dictionary<string, PixelRect>();
    IReadOnlyDictionary<string, DetectPicture> _detectPictures = new Dictionary<string, DetectPicture>();
    Viewport _detectView;
    Core.Domain.Image? _detectShotImage;   // the same picture as the reads see it (the green on a placed box)
    (ImageSource Picture, PixelRect Area)? _detectShot;   // frozen once at 화면 찍기, reused by every redraw
    readonly Dictionary<Core.Domain.Image, ImageSource> _detectBitmaps = [];   // each engine picture made once, by its image
    DetectPicture? _detectLive;
    string? _detectPicked, _detectLiveId;
    bool _detectPaused, _detectZoom, _detectHooked;
    string _detectDrawn = "";

    /// <summary>Turns the engine's Live capture on for one box, or off (null).</summary>
    public Action<string?>? DetectLive { get; set; }

    void FillDetect()
    {
        const string lead = "게임 화면에서 기능마다 읽는 자리입니다. 테두리 색은 기능 맵과 같습니다. ✓ 감지됨, ? 아직 찾는 중, ! 읽어야 하는데 못 읽음. "
            + "✓와 !는 표시가 바뀐 순간의 화면을 보여 줍니다. 칸을 누르면 아래에서 Live로 지금 화면을 보거나 확대하고, 그 칸이 읽은 값을 볼 수 있습니다. 스킬창, 버프, 디버프 줄, 암수, 전분 칸은 눌러서 바로 끌어 옮길 수 있습니다. 화면 찍기는 지금 화면 전체를 뒤에 깔아 주고, 저장하지는 않습니다.";
        var p = Refill(_secDetect, SecDetect, lead);
        if (!_detectHooked)
        {
            _detectHooked = true;
            HookPlace();
            _secDetect.IsVisibleChanged += (_, _) => { if (!_secDetect.IsVisible) { SetLive(null); EndPlace(); DrawTools(); } };   // leaving the page: Live off
            StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { SetLive(null); EndPlace(); DrawTools(); } };   // minimized stays IsVisible
        }
        var shoot = new Button { Content = "화면 찍기", Padding = new(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
        shoot.Click += async (_, _) =>
        {
            if (TakeShot is null) return;
            var (picture, area) = await TakeShot();
            (_detectShot, _detectShotImage) = (picture is null ? null : (Bitmap(picture), area), picture);
            RefreshGreen();
            DrawDetect();
        };
        p.Children.Add(Row("게임 화면", shoot));
        Detach(_detectScreen);
        Detach(_detectTools);
        Detach(_detectDetail);
        Detach(_diag);
        var host = new Viewbox { Child = _detectScreen, Stretch = Stretch.Uniform, MaxHeight = 560, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
        CardStack.SetBreak(host, true);
        p.Children.Add(host);
        p.Children.Add(_detectTools);
        p.Children.Add(_detectDetail);
        p.Children.Add(_diag);
        _detectDrawn = "";
        DrawDetect();
        UiBuild.Report?.Invoke(SecDetect, lead, host, null);
        UiBuild.Report?.Invoke(SecDiag, "칸을 누르면 그 칸이 화면에서 잘라 낸 그림과 읽은 값, 상태를 화면 아래 표로 보여 줍니다. 인식이 이상할 때 어느 칸이 어떻게 읽히는지 확인하세요. 이 화면에서 칸을 누르고 있는 동안만 채워집니다.", _diag, null);
    }

    /// <summary>From the engine's snapshot: redraws only while the page shows and only when an area, a mark or a picture changed.</summary>
    void DetectAreasChanged(Snapshot mine)
    {
        (_detectAreas, _detectView, _detectPictures) = (mine.DetectAreas, mine.DetectView, mine.DetectPictures);
        if (!_detectPaused) _detectLive = mine.DetectLive is { } picture && picture.Id == _detectLiveId ? picture : null;   // paused: the last picture stays; a late one of another box never shows
        foreach (var gone in _detectBitmaps.Keys.Where(k => k != _detectLive?.Picture && !_detectPictures.Values.Any(p => p.Picture == k)).ToList()) _detectBitmaps.Remove(gone);
        if (_secDetect.IsVisible && DetectSignature() != _detectDrawn) { RefreshGreen(); DrawDetect(); }
        if (_mapLivePicture is { } shown && _detectLive is { } live) shown.Source = BitmapOf(live);   // 표시 map: the chip's card follows Live
    }

    string DetectSignature() => string.Join(";", _detectAreas.Select(a => $"{a.Key}{a.Value}{_detect.GetValueOrDefault(a.Key)?.Mark}{_detectPictures.GetValueOrDefault(a.Key)?.At}"))
        + _detectView + _detectLive?.At;

    void SetLive(string? id)
    {
        if (id is null && _detectLiveId is null) return;
        (_detectLiveId, _detectPaused, _detectLive) = (id, false, null);   // another box's (or a paused) picture never shows under this one
        DetectLive?.Invoke(id);
    }

    void DrawDetect()
    {
        _detectDrawn = DetectSignature();
        _detectScreen.Children.Clear();
        var areas = ShownAreas();
        // The screen: the picture's area when one was taken, else the game area the reads use; 확대 shows the picked box's.
        var screen = _detectShot is { } shot ? shot.Area : new PixelRect(_detectView.X, _detectView.Y, _detectView.Width, _detectView.Height);
        if (_detectZoom && _detectPicked is { } z && areas.TryGetValue(z, out var zoomed) && DetectBoard.Around(zoomed, _detectView) is { } around) screen = around;
        _detectShown = screen;
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            (_detectScreen.Width, _detectScreen.Height) = (640, 360);
            _detectScreen.Children.Add(new TextBlock { Text = "게임 화면을 아직 찾지 못했습니다.", Margin = new(16) });
            DrawTools();
            return;
        }
        (_detectScreen.Width, _detectScreen.Height) = (screen.Width, screen.Height);
        _detectScreen.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F));
        if (_detectShot is { } s) PlacePicture(s.Picture, s.Area, screen);
        double stroke = Math.Max(2, screen.Width / 640.0), font = Math.Max(12, screen.Width / 90.0);
        foreach (var (id, area) in areas.OrderBy(a => a.Key == _detectPicked ? 1 : 0))   // the picked one on top
        {
            if (LookOf(id) is not { } look) continue;
            var d = _detect.GetValueOrDefault(id);
            // Live (or paused) for this box, else the stored moment of a ✓ / !; a ? shows none.
            var picture = id == _detectLiveId && _detectLive is { } live ? live : d is { Mark: not DetectMark.Waiting } ? _detectPictures.GetValueOrDefault(id) : null;
            if (picture is not null) PlacePicture(BitmapOf(picture), picture.Area, screen);
            var colour = (Color)ColorConverter.ConvertFromString(look.Color);
            var mark = id == DetectPlace.DebuffRow ? "" : d is null ? " (꺼짐)" : d.Mark switch { DetectMark.Concrete => " ✓", DetectMark.Failed => " !", _ => " ?" };
            bool movable = _place is not null && DetectPlace.PartOf(id) is not null;
            var frame = new Rectangle
            {
                Width = Math.Max(4, area.Width), Height = Math.Max(4, area.Height), Stroke = new SolidColorBrush(colour), StrokeThickness = id == _detectPicked ? stroke * 2 : stroke,
                Fill = Green(id) == true ? GreenFill : Brushes.Transparent, Cursor = DetectPlace.PartOf(id) is not null ? Cursors.SizeAll : Cursors.Hand,
                StrokeDashArray = d is null or { Mark: DetectMark.Waiting } && id != DetectPlace.DebuffRow ? [3, 2] : null,
                Tag = id, Opacity = _place is not null && !movable ? 0.45 : 1,   // placing: what can't be moved steps back
            };
            var label = new Border
            {
                Background = new SolidColorBrush(colour), Padding = new(4, 0, 4, 0), IsHitTestVisible = false,
                Child = new TextBlock { Text = look.Title + mark + (id == _detectLiveId ? (_detectPaused ? " · 일시정지" : " · Live") : ""), Foreground = Brushes.White, FontSize = font },
            };
            Canvas.SetLeft(frame, area.X - screen.X);
            Canvas.SetTop(frame, area.Y - screen.Y);
            Canvas.SetLeft(label, area.X - screen.X);
            Canvas.SetTop(label, Math.Max(0, area.Y - screen.Y - font * 1.5));
            frame.MouseLeftButtonUp += (_, _) => { _detectPicked = id; _placeNote = ""; DrawDetect(); };
            _detectScreen.Children.Add(frame);
            _detectScreen.Children.Add(label);
            if (movable && DetectPlace.PartOf(id) is { } part) AddHandle(part, colour, screen);
        }
        DrawTools();
    }

    /// <summary>The picked box's buttons, its result, and its table rows under the screen.</summary>
    void DrawTools()
    {
        _detectTools.Children.Clear();
        _detectDetail.Text = DetectDetail() + PlaceLine();
        _diag.ShowOnly(_detectPicked is { } shown ? DetectPlace.TableOf(shown) : null);
        DiagnosticsMaybeChanged();
        if (_detectPicked is not { } id || !ShownAreas().ContainsKey(id)) return;
        bool live = _detectLiveId == id;
        if (_detectAreas.ContainsKey(id)) Tool(live ? "Live 끄기" : "Live 켜기", () => SetLive(live ? null : id));
        if (live) Tool(_detectPaused ? "계속" : "일시정지", () =>
        {
            _detectPaused = !_detectPaused;
            DetectLive?.Invoke(_detectPaused ? null : id);   // paused: nothing is captured
            RefreshGreen();
        });
        Tool(_detectZoom ? "전체" : "확대", () => _detectZoom = !_detectZoom);
        PlaceTools();
    }

    Button Tool(string text, Action act, bool enabled = true)
    {
        var b = new Button { Content = text, Padding = new(10, 3, 10, 3), Margin = new(0, 0, 6, 0), IsEnabled = enabled };
        b.Click += (_, _) => { act(); DrawDetect(); };
        _detectTools.Children.Add(b);
        return b;
    }

    /// <summary>A picture at its screen place on the shown screen.</summary>
    void PlacePicture(ImageSource picture, PixelRect at, PixelRect screen)
    {
        var img = new System.Windows.Controls.Image { Source = picture, Width = at.Width, Height = at.Height, IsHitTestVisible = false };
        Canvas.SetLeft(img, at.X - screen.X);
        Canvas.SetTop(img, at.Y - screen.Y);
        _detectScreen.Children.Add(img);
    }

    ImageSource BitmapOf(DetectPicture p)   // Bitmap: frozen (SettingsWindow.Records)
    {
        if (!_detectBitmaps.TryGetValue(p.Picture, out var b)) _detectBitmaps[p.Picture] = b = Bitmap(p.Picture);
        return b;
    }

    /// <summary>The picked box's result: its mark, since when, why, and where it reads.</summary>
    string DetectDetail()
    {
        if (_detectPicked is not { } id || LookOf(id) is not { } box) return "칸을 누르면 그 기능의 결과가 여기 나옵니다.";
        var where = ShownAreas().TryGetValue(id, out var a) ? $"{a.Width}×{a.Height} @ ({a.X}, {a.Y})" : "자리 없음";
        if (id == DetectPlace.DebuffRow) return $"{box.Title}: 내 디버프가 뜨는 줄입니다. 읽는 자리 {where}.";
        if (_detect.GetValueOrDefault(id) is not { } d) return $"{box.Title}: 꺼져 있습니다. 읽는 위치 {where}.";
        string what = d.Mark switch { DetectMark.Concrete => "감지됨", DetectMark.Failed => "읽어야 하는데 못 읽음", _ => "아직 찾는 중" };
        return $"{box.Title}: {what} ({FeatureMapView.Ago(_detectAt - d.At)}부터). 읽는 자리 {where}." + (d.Why is { } why ? $" {why}" : "");
    }
}
