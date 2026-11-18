using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;
using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>화면 인식 설정 (Phase 5): the game screen at its proportions with one thin box per reader where it reads, in its
/// map colour with its 감지 중 mark. A ✓ or ! box shows the engine's picture of the moment it turned; a ? box is dashed with no
/// picture. A click picks a box: under the screen its Live (that box only, 2/s), 일시정지 (keeps the picture, captures
/// nothing), 확대 (the screen zoomed onto it) and its result. 화면 찍기 puts the whole screen behind (kept in memory, never
/// saved). Leaving the page turns Live off. A box 위치 조정 moves can be placed here too (SettingsWindow.DetectPlace.cs), and the
/// picked box's rows of the old 진단 table sit under the screen.</summary>
public partial class SettingsWindow
{
    const string SecDetect = AreaDetect;   // was 읽는 자리, a section of 화면·인식 (rail-split.plan.md)
    readonly CardStack _secDetect = new();
    readonly Canvas _detectScreen = new() { ClipToBounds = true };
    readonly WrapPanel _detectTools = new() { Margin = new(0, Tokens.Space.Gap, 0, 0) };
    readonly TextBlock _detectDetail = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Gap, 0, 0) };
    IReadOnlyDictionary<string, PixelRect> _detectAreas = new Dictionary<string, PixelRect>();
    IReadOnlyDictionary<string, DetectPicture> _detectPictures = new Dictionary<string, DetectPicture>();
    IReadOnlyDictionary<string, IReadOnlyList<GridLine>> _detectGrid = new Dictionary<string, IReadOnlyList<GridLine>>();
    Viewport _detectView;
    Core.Domain.Image? _detectShotImage;   // the same picture as the reads see it (the green on a placed box)
    (ImageSource Picture, PixelRect Area)? _detectShot;   // frozen once at 화면 찍기, reused by every redraw
    readonly Dictionary<Core.Domain.Image, ImageSource> _detectBitmaps = [];   // each engine picture made once, by its image
    DetectPicture? _detectLive;
    string? _detectPicked, _detectLiveId;
    bool _detectPaused, _detectHooked;
    string _detectDrawn = "";

    /// <summary>Turns the engine's Live capture on for one box, or off (null).</summary>
    public Action<string?>? DetectLive { get; set; }

    void FillDetect()
    {
        const string lead = "게임 화면에서 기능마다 읽는 자리입니다. 테두리 색은 기능 맵과 같습니다. ✓ 읽는 중, ? 아직 찾는 중, ! 읽어야 하는데 못 읽음. "
            + "✓와 !는 표시가 바뀐 순간의 화면을 보여 줍니다. 칸을 누르면 위의 Live로 지금 화면을 보고, 아래에서 그 칸이 읽은 값을 봅니다. 칸 모서리의 자리 맞추기를 누르면(Enter, 두 번 누르기) 크게 보며 옮길 수 있습니다. 저장한 화면에 없는 칸은 테두리만 보입니다.";
        var p = Refill(_secDetect, SecDetect, lead);
        if (!_detectHooked)
        {
            _detectHooked = true;
            HookPlace();
            HookOpenPlace();
            _secDetect.IsVisibleChanged += (_, _) => { if (!_secDetect.IsVisible) { SetLive(null); _placeWindow?.Close(); EndPlace(); DrawTools(); } };   // leaving the page: Live off
            StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { SetLive(null); _placeWindow?.Close(); EndPlace(); DrawTools(); } };   // minimized stays IsVisible
        }
        ReadingRows(p);
        _placeWindow?.Close();   // a refill (profile switch, tray pick) re-hosts the canvas: the zoom window gives it back first
        Detach(_detectScreen);
        Detach(_detectTools);
        Detach(_detectDetail);
        Detach(_placeExample);
        Detach(_diag);
        var host = new Viewbox { Child = _detectScreen, Stretch = Stretch.Uniform, MaxHeight = 560, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, Tokens.Space.Gap, 0, 0) };
        CardStack.SetBreak(host, true);
        p.Children.Add(_detectTools);   // Live above the screen; 자리 맞추기 sits on the picked box (user, 2026-11-17)
        p.Children.Add(host);
        p.Children.Add(_detectDetail);
        p.Children.Add(_placeExample);
        p.Children.Add(_diag);
        _detectDrawn = "";
        DrawDetect();
        UiBuild.Report?.Invoke(SecDetect, lead, host, null);
        UiBuild.Report?.Invoke(SecDiag, "칸을 누르면 그 칸이 화면에서 잘라 낸 그림과 읽은 값, 상태를 화면 아래 표로 보여 줍니다. 인식이 이상할 때 어느 칸이 어떻게 읽히는지 확인하세요. 이 화면에서 칸을 누르고 있는 동안만 채워집니다.", _diag, null);
    }

    /// <summary>From the engine's snapshot: redraws only while the page shows and only when an area, a mark or a picture changed.</summary>
    void DetectAreasChanged(Snapshot mine)
    {
        (_detectAreas, _detectView, _detectPictures, _detectGrid) = (mine.DetectAreas, mine.DetectView, mine.DetectPictures, mine.DetectGrid);
        if (!_detectPaused) _detectLive = mine.DetectLive is { } picture && picture.Id == _detectLiveId ? picture : null;   // paused: the last picture stays; a late one of another box never shows
        foreach (var gone in _detectBitmaps.Keys.Where(k => k != _detectLive?.Picture && !_detectPictures.Values.Any(p => p.Picture == k)).ToList()) _detectBitmaps.Remove(gone);
        if (_secDetect.IsVisible) ReadingViewChanged();   // another game-area size: its own saved pictures
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
        if (_placeFrame is { } framed) screen = framed;   // the zoom window: framed on its box
        _detectShown = screen;
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            (_detectScreen.Width, _detectScreen.Height) = (640, 360);
            // Empty form (system.md "States"): what is empty in Body, why and the next step in Hint.
            var empty = new StackPanel { Margin = new(Tokens.Space.Card) };
            empty.Children.Add(new TextBlock { Text = "게임 화면을 아직 찾지 못했습니다.", FontSize = Tokens.Font.Body });
            empty.Children.Add(UiBuild.Muted(new TextBlock { Text = "로스트아크를 실행하면 기능마다 읽는 자리가 보입니다.", FontSize = Tokens.Font.Hint, TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Tight, 0, 0) }));
            // The canvas sits in a Viewbox, which scales its text: size it back to the tokens once the scale is known.
            // ponytail: set at load, not on every window resize (the text then drifts a little until the page is redrawn).
            // From the tokens each time, never from the current size: Loaded fires again on every return to the tab, and
            // dividing the current size shrank the text a step each visit.
            empty.Loaded += (_, _) =>
            {
                double k = ShownScale;
                if (k <= 0 || _detectScreen.Parent is not Viewbox) return;
                ((TextBlock)empty.Children[0]).FontSize = Tokens.Font.Body / k;
                ((TextBlock)empty.Children[1]).FontSize = Tokens.Font.Hint / k;
                empty.Margin = new(Tokens.Space.Card / k);
                ((TextBlock)empty.Children[1]).Margin = new(0, Tokens.Space.Tight / k, 0, 0);
            };
            _detectScreen.Children.Add(empty);
            DrawTools();
            return;
        }
        double sampleTop = PlaceSampleOffset(screen);   // 예시 보기: a panel under the capture
        (_detectScreen.Width, _detectScreen.Height) = (screen.Width, sampleTop > 0 ? sampleTop + screen.Height : screen.Height);
        _detectScreen.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F));
        if (_detectShot is { } s) PlacePicture(s.Picture, s.Area, screen);
        double stroke = Math.Max(2, screen.Width / 640.0), font = Math.Max(12, screen.Width / 90.0);
        foreach (var (id, area) in areas.OrderBy(a => a.Key == _detectPicked ? 1 : 0))   // the picked one on top
        {
            if (LookOf(id) is not { } look) continue;
            var d = _detect.GetValueOrDefault(id);
            // Live (or paused) for this box, else the stored moment of a ✓ / !; a ? shows none.
            bool off = OffPicture(id);   // not on the saved picture shown: outline only, still picked and placed (user, 2026-11-17)
            var picture = off ? null : id == _detectLiveId && _detectLive is { } live ? live : d is { Mark: not DetectMark.Waiting } ? _detectPictures.GetValueOrDefault(id) : null;
            if (picture is not null) PlacePicture(BitmapOf(picture), picture.Area, screen);
            var colour = (Color)ColorConverter.ConvertFromString(look.Color);
            var mark = id == DetectPlace.DebuffRow ? "" : d is null ? " (꺼짐)" : d.Mark switch { DetectMark.Concrete => " ✓", DetectMark.Failed => " !", _ => " ?" };
            bool movable = _place is not null && DetectPlace.PartOf(id) is not null;
            var frame = new Rectangle
            {
                Width = Math.Max(4, area.Width), Height = Math.Max(4, area.Height), Stroke = new SolidColorBrush(colour), StrokeThickness = id == _detectPicked ? stroke * 2 : stroke,
                Fill = Green(id) == true ? GreenFill : Brushes.Transparent, Cursor = DetectPlace.PartOf(id) is not null ? Cursors.SizeAll : Cursors.Hand,
                StrokeDashArray = off || d is null or { Mark: DetectMark.Waiting } && id != DetectPlace.DebuffRow ? [3, 2] : null,
                Tag = id, Opacity = off || _place is not null && !movable ? 0.45 : 1,   // placing: what can't be moved steps back
            };
            var label = new Border
            {
                Background = new SolidColorBrush(colour), Padding = new(Tokens.Space.Tight, 0, Tokens.Space.Tight, 0), IsHitTestVisible = false,
                Child = new TextBlock { Text = look.Title + mark + (id == _detectLiveId ? (_detectPaused ? " · 일시정지" : " · Live") : ""), Foreground = Brushes.White, FontSize = font },
            };
            // A box being placed draws its own guides. The full view only shows where things are (no adjusting there), so its
            // grids are hairlines that don't crowd the boxes (user, 2026-11-18); the zoom window keeps them readable.
            if (!movable) AddGrid(id, area, colour, _placeWindow is null ? Px(FullViewGridPx) : stroke / 2, screen, _placeWindow is null ? FullViewGridOpacity : GridOpacity);
            Canvas.SetLeft(frame, area.X - screen.X);
            Canvas.SetTop(frame, area.Y - screen.Y);
            Canvas.SetLeft(label, area.X - screen.X);
            Canvas.SetTop(label, Math.Max(0, area.Y - screen.Y - font * 1.5));
            frame.MouseLeftButtonUp += (_, _) => { _detectPicked = id; _placeNote = ""; _detectScreen.Focus(); DrawDetect(); };
            _detectScreen.Children.Add(frame);
            _detectScreen.Children.Add(label);
            if (movable && DetectPlace.PartOf(id) is { } part)
            {
                AddGuides(part, colour, screen);
                AddHandle(part, colour, screen);
            }
        }
        DrawPlaceSample(screen, sampleTop);
        if (_detectPicked is { } picked && areas.TryGetValue(picked, out var pickedArea)) AddPlaceButton(pickedArea, screen);   // over the 예시 panel too
        DrawTools();
    }

    /// <summary>The picked box's buttons, its result, and its table rows under the screen.</summary>
    void DrawTools()
    {
        _detectTools.Children.Clear();
        _detectDetail.Text = DetectDetail() + (_placeWindow is null ? PlaceLine() : "");
        PlaceWindowState();
        ShowPlaceExample();
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
    }

    /// <summary>자리 맞추기 on the picked box's top-right corner, at its usual size whatever the screen's scale (the page only: the zoom
    /// window is already placing). Right of the box, or inside its right edge where the screen ends.</summary>
    void AddPlaceButton(PixelRect area, PixelRect screen)
    {
        if (_placeWindow is not null) return;
        var b = new Button { Content = "자리 맞추기", Padding = new(Tokens.Space.Gap, 0, Tokens.Space.Gap, 0), ToolTip = "크게 보며 이 칸을 옮깁니다. Enter나 칸을 두 번 눌러도 됩니다." };
        double k = Math.Max(ShownScale, 0.05);
        b.LayoutTransform = new ScaleTransform(1 / k, 1 / k);
        b.Click += (_, _) => OpenPlace();
        _detectScreen.Children.Add(b);
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = b.DesiredSize.Width, h = b.DesiredSize.Height, gap = Px(Tokens.Space.Tight);
        double left = area.Right - screen.X + gap;
        if (left + w > screen.Width) left = Math.Max(0, area.Right - screen.X - w);
        Canvas.SetLeft(b, left);
        Canvas.SetTop(b, Math.Clamp(area.Y - screen.Y - h - gap, 0, Math.Max(0, screen.Height - h)));
    }

    Button Tool(string text, Action act, bool enabled = true)
    {
        var b = new Button { Content = text, Padding = new(Tokens.Space.Gap, Tokens.Space.Tight, Tokens.Space.Gap, Tokens.Space.Tight), Margin = new(0, 0, Tokens.Space.Gap, 0), IsEnabled = enabled };
        b.Click += (_, _) => { act(); DrawDetect(); };
        _detectTools.Children.Add(b);
        return b;
    }

    /// <summary>The box's grid lines (reading-place.plan.md C), from the engine's box onto the shown one: a box being placed
    /// carries its lines along, moved and sized with it.</summary>
    void AddGrid(string id, PixelRect shown, Color colour, double thickness, PixelRect screen, double opacity)
    {
        if (!_detectGrid.TryGetValue(id, out var lines) || !_detectAreas.TryGetValue(id, out var read) || read.Width <= 0 || read.Height <= 0) return;
        double sx = shown.Width / (double)read.Width, sy = shown.Height / (double)read.Height;
        double X(int x) => shown.X + (x - read.X) * sx - screen.X;
        double Y(int y) => shown.Y + (y - read.Y) * sy - screen.Y;
        var brush = new SolidColorBrush(colour) { Opacity = opacity };
        brush.Freeze();
        foreach (var l in lines)
            _detectScreen.Children.Add(new Line { X1 = X(l.X1), Y1 = Y(l.Y1), X2 = X(l.X2), Y2 = Y(l.Y2), Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false });
    }

    const double GridOpacity = 0.7;   // under the box's own edge: the lines read as inside it
    const double FullViewGridPx = 0.5, FullViewGridOpacity = 0.45;   // the full view's grid: one hairline per cell edge

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
        if (_detect.GetValueOrDefault(id) is not { } d) return $"{box.Title}: 꺼져 있습니다. 읽는 자리 {where}.";
        string what = d.Mark switch { DetectMark.Concrete => "읽는 중", DetectMark.Failed => "읽어야 하는데 못 읽음", _ => "아직 찾는 중" };
        return $"{box.Title}: {what} ({FeatureMapView.Ago(_detectAt - d.At)}부터). 읽는 자리 {where}." + (d.Why is { } why ? $" {why}" : "");
    }
}
