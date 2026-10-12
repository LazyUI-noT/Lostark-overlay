using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Image = System.Windows.Controls.Image;
using Geometry = GaugeOverlay.Core.Vision.Geometry;

namespace GaugeOverlay.App.UI;

/// <summary>위치 조정 (수동 조정), folded on the window's right until opened (the window widens for it): a tab per part with a live
/// picture and the boxes the reads use; the boxes are dragged (the skill bar's bottom-left corner scales it) or moved and sized with
/// sliders and ± buttons, all in screen pixels that only their own slider changes. The picture zooms with the wheel, pans with the
/// right or middle button, resets on a double click, and its height follows the grip under it. Every change shows in the live
/// reading at once (<see cref="AdjustChanged"/>), but only while the panel is open; 적용 (<see cref="ApplyRequested"/>) keeps it.
/// The window never takes the foreground from the game and is left out of captures, so it can sit over the game.</summary>
public partial class DebugWindow
{
    /// <summary>A new fit and game 버프 크기 to preview, for session gen.</summary>
    public event Action<int, Fit, int>? AdjustChanged;
    /// <summary>적용: the game area, the fit, the game's 버프 크기 (a 설정 value), and whether it is 테스트 모드's (then the fit isn't saved).</summary>
    public event Action<Viewport, Fit, int, bool>? ApplyRequested;
    /// <summary><see cref="AdjustOpen"/> changed: the preview runs only while it is true.</summary>
    public event Action? AdjustOpenChanged;

    /// <summary>The 위치 조정 panel is open and the window shown.</summary>
    public bool AdjustOpen => IsVisible && AdjustToggle.IsChecked == true;

    /// <summary>--demo: the window may be captured (screenshots of it); otherwise it is left out of every capture.</summary>
    public bool Capturable { get; set; }

    const double PanelMin = 320, PanelMax = 1400;   // the panel's width range (DIP); it opens at the XAML's 440
    const double PanelChrome = 6 + 26 + 20;          // the resize edge, the margins and the scroll bar around a picture
    const double Handle = 10;       // DIP around the skill bar's bottom-left corner that scales it
    const double MaxMove = 400;     // a slider's reach in screen pixels
    const double MaxZoom = 8, ZoomStep = 1.25;
    const double MinView = 40, MaxView = 1200;       // a picture's height range (DIP), set by its grip
    const int BuffStep = 10;                         // the game's 버프 크기 choices are 10% apart

    static readonly (AdjustPart Part, string Name, Color Color)[] Parts =
    [
        (AdjustPart.스킬창, "스킬창", Color.FromRgb(0xFF, 0xD2, 0x4A)), (AdjustPart.버프, "버프 줄", Color.FromRgb(0x1B, 0xA8, 0xE0)),
        (AdjustPart.디버프, "디버프 줄", Color.FromRgb(0xE8, 0x26, 0x2B)), (AdjustPart.보스, "보스 줄", Color.FromRgb(0xA0, 0x4F, 0xB0)),
    ];

    FitAdjust? _adj;
    int _gen;
    bool _test, _syncing;
    (FitAdjust Start, AdjustPart Part, bool Corner, Point At)? _drag;
    readonly Dictionary<AdjustPart, Pane> _panes = [];   // the boss's picture for 보스, the bottom band for the others
    readonly Dictionary<AdjustPart, Pane> _guides = [];  // 예시: a shot with its boxes and lines, per tab

    /// <summary>The 예시 shots (App/Assets/guide, cut from mordum.png and "mordum debuff3.png": 1920×1080, HUD 110, 버프 120%) and the
    /// screen rect each shows; their boxes and lines come from <see cref="GuideFit"/> like the live ones.</summary>
    static readonly Dictionary<AdjustPart, (string File, PixelRect At)> GuideShots = new()
    {
        [AdjustPart.스킬창] = ("bar.png", new(480, 930, 600, 150)), [AdjustPart.버프] = ("buff.png", new(550, 880, 310, 70)),
        [AdjustPart.디버프] = ("debuff.png", new(1060, 880, 200, 65)), [AdjustPart.보스] = ("boss.png", new(690, 130, 560, 90)),
    };
    static readonly FitAdjust GuideFit = FitAdjust.Begin(new Viewport(0, 0, 1920, 1080), 120, new Fit(110));
    readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    readonly TabControl _tabs = new();
    readonly List<(AdjustPart Part, char Axis, Slider Slider, TextBlock Value)> _sliders = [];
    readonly Button _revert = new() { Content = "되돌리기", Padding = new(14, 4, 14, 4), Margin = new(0, 0, 8, 0) };
    readonly Button _apply = new() { Content = "적용", Padding = new(14, 4, 14, 4) };
    readonly ToggleButton _freeze = new() { Content = "화면 멈춤", Padding = new(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 8),
        ToolTip = "위치 조정 화면만 멈춥니다. 읽기와 오버레이는 그대로 움직입니다." };

    /// <summary>One picture with the boxes drawn over it, as wide as the panel allows (<see cref="Fit"/>), as tall as its grip
    /// makes it, zoomed and panned inside that frame. Points on <see cref="Boxes"/> are in unzoomed picture DIP.</summary>
    sealed class Pane
    {
        public readonly Grid Root = new() { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Black, ClipToBounds = true };
        public readonly Grid Content = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        public readonly Image Picture = new() { Stretch = Stretch.Fill };
        public readonly Canvas Boxes = new() { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        public readonly Thumb Grip = new() { Height = 7, Cursor = Cursors.SizeNS, Opacity = 0.35, Margin = new(0, 1, 0, 0), ToolTip = "끌어서 화면 높이를 바꿉니다." };
        readonly ScaleTransform _zoom = new();
        readonly TranslateTransform _pan = new();
        public PixelRect Rect;      // the screen area shown, in screen pixels
        public double? ViewHeight;  // set by the grip; null: the picture's own shape
        public double Zoom => _zoom.ScaleX;
        public double Scale => Rect.Width > 0 ? Content.Width / Rect.Width : 1;

        public Pane()
        {
            Content.Children.Add(Picture);
            Content.Children.Add(Boxes);
            Content.RenderTransform = new TransformGroup { Children = { _zoom, _pan } };
            Root.Children.Add(Content);
            _zoom.ScaleX = _zoom.ScaleY = 1;
        }

        /// <summary><paramref name="width"/> DIP wide; the picture keeps its shape, the frame its height.</summary>
        public void Fit(double width)
        {
            Content.Width = Root.Width = Math.Max(0, width);
            Content.Height = Rect.Height * Scale;
            Root.Height = ViewHeight ?? Content.Height;
            Pan(0, 0);
        }

        /// <summary>The wheel: zoom by <paramref name="factor"/> keeping the picture point under <paramref name="at"/> (frame DIP).</summary>
        public void ZoomAt(Point at, double factor)
        {
            double z = Math.Clamp(Zoom * factor, 1, MaxZoom);
            double px = (at.X - _pan.X) / Zoom, py = (at.Y - _pan.Y) / Zoom;
            _zoom.ScaleX = _zoom.ScaleY = z;
            (_pan.X, _pan.Y) = (at.X - px * z, at.Y - py * z);
            Pan(0, 0);
        }

        /// <summary>Moves the zoomed picture, never past its edges.</summary>
        public void Pan(double dx, double dy)
        {
            _pan.X = Math.Clamp(_pan.X + dx, Math.Min(0, Root.Width - Content.Width * Zoom), 0);
            _pan.Y = Math.Clamp(_pan.Y + dy, Math.Min(0, Root.Height - Content.Height * Zoom), 0);
        }

        /// <summary>Zooms and pans so the screen rect <paramref name="r"/> fills the frame, all of it in view, with
        /// <paramref name="margin"/> of its size around it.</summary>
        public void ZoomTo(PixelRect r, double margin)
        {
            if (Root.Width <= 0 || Root.Height <= 0 || r.Width <= 0 || r.Height <= 0) return;
            double s = Scale, w = r.Width * s * (1 + 2 * margin), h = r.Height * s * (1 + 2 * margin);
            double z = Math.Clamp(Math.Min(Root.Width / w, Root.Height / h), 1, MaxZoom);
            double cx = (r.X + r.Width / 2.0 - Rect.X) * s, cy = (r.Y + r.Height / 2.0 - Rect.Y) * s;
            _zoom.ScaleX = _zoom.ScaleY = z;
            (_pan.X, _pan.Y) = (Root.Width / 2 - cx * z, Root.Height / 2 - cy * z);
            Pan(0, 0);   // at the picture's edge the view stops there; the box still fits, being no bigger than the frame
        }

        public void ResetZoom()
        {
            _zoom.ScaleX = _zoom.ScaleY = 1;
            (_pan.X, _pan.Y) = (0, 0);
        }

        /// <summary>A point on the picture (unzoomed DIP) as a screen pixel.</summary>
        public (double X, double Y) ToScreen(Point p) => (Rect.X + p.X / Scale, Rect.Y + p.Y / Scale);
    }

    void BuildAdjust()
    {
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!Capturable) WindowStyles.SetExcludeFromCapture(hwnd, true);   // never in its own pictures or the reads
            WindowStyles.Set(hwnd, ExStyle.NoActivate, true);                   // the game stays in front
        };
        AdjustToggle.Checked += (_, _) => Fold(open: true);
        AdjustToggle.Unchecked += (_, _) => Fold(open: false);
        AdjustResize.DragDelta += (_, e) =>
        {
            // Dragging the panel's left edge: the panel and the window grow or shrink together, the table keeps its width.
            double old = AdjustPanel.Width, now = Math.Clamp(old - e.HorizontalChange, PanelMin, PanelMax);
            AdjustPanel.Width = now;
            if (WindowState == WindowState.Normal) Width += now - old;
            FitPanes();
        };
        IsVisibleChanged += (_, _) => AdjustOpenChanged?.Invoke();

        AdjustHost.Children.Add(new TextBlock { Text = "수동 조정", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) });
        AdjustHost.Children.Add(_note);
        AdjustHost.Children.Add(_freeze);
        foreach (var (part, name, color) in Parts)
        {
            var pane = new Pane();
            Wire(pane);
            _panes[part] = pane;
            var body = new StackPanel { Margin = new(0, 8, 0, 0) };
            var guide = new Pane();
            guide.Boxes.Cursor = null;
            (guide.Picture.Source, guide.Rect) = (new BitmapImage(new Uri($"pack://application:,,,/Assets/guide/{GuideShots[part].File}")), GuideShots[part].At);
            _guides[part] = guide;
            body.Children.Add(new TextBlock { Text = "예시 (HUD 110%, 버프 120%): 상자와 선이 이렇게 놓이면 맞습니다.", Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 4) });
            body.Children.Add(guide.Root);
            body.Children.Add(new TextBlock { Text = part == AdjustPart.보스 ? "실시간: 화면 위쪽" : "실시간: 화면 아래쪽", Opacity = 0.75, Margin = new(0, 10, 0, 4) });
            body.Children.Add(pane.Root);
            body.Children.Add(pane.Grip);
            body.Children.Add(SliderRow("가로 (px)", part, 'x', -MaxMove, MaxMove));
            body.Children.Add(SliderRow("세로 (px)", part, 'y', -MaxMove, MaxMove));
            if (part == AdjustPart.스킬창)
            {
                body.Children.Add(SliderRow("크기 (HUD %)", part, 's', FitAdjust.MinHud, FitAdjust.MaxHud));
                body.Children.Add(Hint("스킬창을 옮기거나 키우면 버프·디버프 줄도 함께 따라갑니다. 보스 줄은 크기만 따라갑니다. 크기를 바꿔도 가로·세로 값은 그대로입니다."));
            }
            else
            {
                if (part == AdjustPart.보스)
                {
                    body.Children.Add(SliderRow("크기 (%)", part, 'p', FitAdjust.MinBoss, FitAdjust.MaxBoss));
                    body.Children.Add(Hint("100%가 게임이 그리는 크기입니다. 보스 버프·디버프 사이의 '/'를 기준으로 커집니다."));
                }
                else
                {
                    body.Children.Add(SliderRow("게임의 버프 크기 (%)", part, 'b', Settings.BuffSizes[0], Settings.BuffSizes[^1]));
                    body.Children.Add(Hint("게임 설정의 버프 크기입니다. 버프 줄과 디버프 줄이 같이 쓰고, 적용하면 설정 → 게임의 버프 크기에 저장합니다. 스킬창 맞추기는 버프 크기를 찾지 않습니다."));
                }
            }
            _tabs.Items.Add(new TabItem { Header = new TextBlock { Text = name, Foreground = new SolidColorBrush(color), FontWeight = FontWeights.SemiBold }, Content = body });
        }
        AdjustHost.Children.Add(_tabs);
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != _tabs || _tabs.SelectedIndex < 0) return;   // not a combo box inside a tab
            ZoomToPart(Parts[_tabs.SelectedIndex].Part);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 0) };
        buttons.Children.Add(_revert);
        buttons.Children.Add(_apply);
        AdjustHost.Children.Add(buttons);
        AdjustHost.Children.Add(_result);
        _revert.Click += (_, _) => { if (_adj is not null) Changed(_adj.Reverted()); };
        _apply.Click += (_, _) => { if (_adj is not null) ApplyRequested?.Invoke(_adj.Vp, _adj.Current, _adj.BuffSize, _test); };
        StopAdjust();
        FitGuides();
    }

    static TextBlock Hint(string text) => new() { Text = text, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) };

    /// <summary>Opens or folds the panel, widening or narrowing the window by it.</summary>
    void Fold(bool open)
    {
        AdjustPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AdjustToggleText.Text = open ? "위치 조정 ▸" : "위치 조정 ◂";
        if (WindowState == WindowState.Normal) Width = Math.Max(MinWidth, Width + (open ? AdjustPanel.Width : -AdjustPanel.Width));
        AdjustOpenChanged?.Invoke();
    }

    /// <summary>설정's "디버그 창에서 위치 조정": the window with its panel open.</summary>
    public void ShowAdjust()
    {
        Show();
        AdjustToggle.IsChecked = true;
    }

    /// <summary>A preview session began (the panel opened, or the game area or fit changed): its boxes start on <paramref name="fit"/>.</summary>
    public void StartAdjust(int gen, Viewport vp, Fit fit, int buffSize, bool test)
    {
        (_gen, _test, _drag) = (gen, test, null);
        if (_adj is null || _adj.Vp != vp)
        {
            // Another game area: a frozen picture of the old one would sit under boxes for the new one.
            _freeze.IsChecked = false;
            foreach (var pane in _panes.Values) (pane.Picture.Source, pane.Rect) = (null, default);
        }
        _adj = FitAdjust.Begin(vp, buffSize, fit);
        _note.Text = "상자를 끌어 옮기고, 스킬창 왼쪽 아래 모서리를 끌어 크기를 바꿉니다. ± 버튼은 1px씩 바꿉니다. " +
            "휠로 확대, 오른쪽(가운데) 버튼으로 끌어 이동, 두 번 눌러 원래 크기로 돌아갑니다. 화면 아래 막대를 끌어 높이를 바꿉니다. " +
            "바꾼 위치는 이 칸을 펼친 동안만 쓰이고, 적용하지 않고 접거나 닫으면 원래대로 돌아갑니다.\n" +
            (test ? "테스트 모드: 적용하면 이 영상에만 쓰고 저장하지 않습니다."
                  : "적용하면 수동 조정으로 저장하고, 지금 준비된 스킬 칸을 기준으로 다시 저장합니다. 스킬이 쿨 돈 상태에서 누르세요.");
        _tabs.IsEnabled = _apply.IsEnabled = true;
        Sync();
        Redraw();
        if (_adj.Changed) AdjustChanged?.Invoke(_gen, _adj.Current, _adj.BuffSize);   // the start was outside the game area: clamped
    }

    /// <summary>No preview (panel folded, window closed, no game or no fit yet).</summary>
    public void StopAdjust()
    {
        (_adj, _drag, _freeze.IsChecked) = (null, null, false);
        _note.Text = "게임 화면과 맞춘 스킬창이 있으면 여기에 실시간 화면이 나옵니다. 스킬창을 먼저 맞춰 주세요.";
        _tabs.IsEnabled = _apply.IsEnabled = _revert.IsEnabled = false;
        foreach (var pane in _panes.Values) { pane.Picture.Source = null; pane.Boxes.Children.Clear(); pane.Rect = default; pane.ResetZoom(); pane.Fit(0); }
    }

    /// <summary>The pictures follow the panel's width.</summary>
    void FitPanes()
    {
        foreach (var pane in _panes.Values) pane.Fit(pane.Rect.Width > 0 ? AdjustPanel.Width - PanelChrome : 0);
        FitGuides();
        Redraw();
    }

    /// <summary>The 예시 pictures follow the panel's width; nothing else changes them, so they are drawn only here.</summary>
    void FitGuides()
    {
        foreach (var (tab, guide) in _guides)
        {
            if (Math.Abs(guide.Root.Width - (AdjustPanel.Width - PanelChrome)) < 0.5 && guide.Boxes.Children.Count > 0) continue;
            guide.Fit(AdjustPanel.Width - PanelChrome);
            guide.Boxes.Children.Clear();
            DrawParts(guide, GuideFit, tab, handle: false);
        }
    }

    /// <summary>적용 was kept: what was saved.</summary>
    public void Committed(string text) => _result.Text = text;

    /// <summary>The session's latest pictures.</summary>
    public void ShowFrame(int gen, Core.Domain.Image bottom, PixelRect bottomRect, Core.Domain.Image top, PixelRect topRect)
    {
        if (gen != _gen || _adj is null) return;
        // 화면 멈춤: the pictures stay as they are (only the boxes move), so a buff that runs out doesn't vanish while lining up.
        if (_freeze.IsChecked == true && _panes.Values.Any(p => p.Picture.Source is not null)) return;
        BitmapSource Bitmap(Core.Domain.Image img) => BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgr32, null, img.Bgra, img.Stride);
        var (low, high) = (Bitmap(bottom), Bitmap(top));
        var fresh = _panes.Where(p => p.Value.Picture.Source is null).Select(p => p.Key).ToList();   // a session's first picture
        foreach (var (part, pane) in _panes)
            (pane.Picture.Source, pane.Rect) = part == AdjustPart.보스 ? (high, topRect) : (low, bottomRect);
        FitPanes();
        foreach (var part in fresh) ZoomToPart(part);
    }

    const double ZoomMargin = 0.15;   // of the box, around it, when a tab zooms to its part

    /// <summary>A tab's picture zoomed to its own part (the whole buff row on 버프 줄, …): when the tab is picked and on a session's
    /// first picture. The wheel, panning and a double click still work after.</summary>
    void ZoomToPart(AdjustPart part)
    {
        if (_adj is not { } a || !_panes.TryGetValue(part, out var pane) || pane.Rect.Width == 0) return;
        pane.ZoomTo(a.RectOf(part), ZoomMargin);
        Redraw();   // line widths follow the zoom
    }

    void Changed(FitAdjust next)
    {
        _adj = next;
        Sync();
        Redraw();
        AdjustChanged?.Invoke(_gen, next.Current, next.BuffSize);
    }

    void Wire(Pane pane)
    {
        pane.Boxes.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { pane.ResetZoom(); Redraw(); e.Handled = true; return; }
            if (_adj is null) return;
            var at = e.GetPosition(pane.Boxes);
            var (x, y) = pane.ToScreen(at);
            if (_adj.PartAt(x, y, Handle / (pane.Scale * pane.Zoom)) is not var (part, corner)) return;
            _drag = (_adj, part, corner, at);
            pane.Boxes.CaptureMouse();
        };
        pane.Boxes.MouseMove += (_, e) =>
        {
            if (_drag is not var (start, part, corner, from)) return;
            var at = e.GetPosition(pane.Boxes);
            double dx = (at.X - from.X) / pane.Scale, dy = (at.Y - from.Y) / pane.Scale;
            // The whole distance from where the drag began, so rounding never drifts.
            Changed(corner ? start.DragLeftTo(start.RectOf(AdjustPart.스킬창).X + dx) : start.Move(part, dx, dy));
        };
        pane.Boxes.MouseLeftButtonUp += (_, _) => { _drag = null; pane.Boxes.ReleaseMouseCapture(); };

        pane.Root.MouseWheel += (_, e) =>
        {
            pane.ZoomAt(e.GetPosition(pane.Root), e.Delta > 0 ? ZoomStep : 1 / ZoomStep);
            Redraw();   // line widths follow the zoom
            e.Handled = true;   // the panel doesn't scroll under a zoom
        };
        Point? panFrom = null;
        pane.Root.MouseDown += (_, e) =>
        {
            // Not during a box drag: taking the capture would lose its button-up and leave the box following the pointer.
            if (e.ChangedButton is not (MouseButton.Right or MouseButton.Middle) || _drag is not null) return;
            panFrom = e.GetPosition(pane.Root);
            pane.Root.CaptureMouse();
            e.Handled = true;
        };
        pane.Root.MouseMove += (_, e) =>
        {
            if (panFrom is not { } from) return;
            var at = e.GetPosition(pane.Root);
            pane.Pan(at.X - from.X, at.Y - from.Y);
            panFrom = at;
        };
        pane.Root.MouseUp += (_, e) =>
        {
            if (panFrom is null || e.ChangedButton is not (MouseButton.Right or MouseButton.Middle)) return;
            panFrom = null;
            pane.Root.ReleaseMouseCapture();
        };
        pane.Grip.DragDelta += (_, e) =>
        {
            if (pane.Rect.Width == 0) return;
            pane.ViewHeight = Math.Clamp(pane.Root.Height + e.VerticalChange, MinView, MaxView);
            pane.Fit(pane.Root.Width);
        };
    }

    UIElement SliderRow(string label, AdjustPart part, char axis, double min, double max)
    {
        var grid = new Grid { Margin = new(0, 2, 0, 2) };
        foreach (var w in new[] { 110.0, 34, 1, 34, 70 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        var slider = new Slider { Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 6, 0) };
        if (axis == 'b') (slider.TickFrequency, slider.IsSnapToTickEnabled, slider.TickPlacement) = (BuffStep, true, TickPlacement.BottomRight);
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0) };
        Button Step(string text, int d)
        {
            var b = new Button { Content = text, MinWidth = 28, MinHeight = 26, Padding = new(0), FontSize = 14 };
            b.Click += (_, _) => Nudge(part, axis, d);
            return b;
        }
        UIElement[] cells = [new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, Step("−", -1), slider, Step("+", 1), value];
        for (int c = 0; c < cells.Length; c++) { Grid.SetColumn(cells[c], c); grid.Children.Add(cells[c]); }
        slider.ValueChanged += (_, e) =>
        {
            if (_syncing || _adj is not { } a) return;
            var (ox, oy) = a.Offset(part);
            Changed(axis switch
            {
                'x' => a.Move(part, e.NewValue - ox, 0),
                'y' => a.Move(part, 0, e.NewValue - oy),
                's' => a.ScaleTo(e.NewValue),
                'b' => a.WithBuffSize((int)Math.Round(e.NewValue)),
                _ => a.BossTo((int)Math.Round(e.NewValue)),
            });
        };
        _sliders.Add((part, axis, slider, value));
        return grid;
    }

    /// <summary>± buttons: one screen pixel (the bar's size: one pixel wider or narrower), the next 버프 크기 the game offers, 1% of the boss's size.</summary>
    void Nudge(AdjustPart part, char axis, int d)
    {
        if (_adj is not { } a) return;
        Changed(axis switch
        {
            'x' => a.Move(part, d, 0),
            'y' => a.Move(part, 0, d),
            's' => a.WidenBy(d),
            'b' => a.WithBuffSize(a.BuffSize + d * BuffStep),
            _ => a.BossTo(a.BossPercent + d),
        });
    }

    /// <summary>The sliders and values show the session (without reacting to it).</summary>
    void Sync()
    {
        if (_adj is not { } a) return;
        _syncing = true;
        foreach (var (part, axis, slider, value) in _sliders)
        {
            var (ox, oy) = a.Offset(part);
            double v = axis switch { 'x' => ox, 'y' => oy, 's' => a.Current.Hud, 'b' => a.BuffSize, _ => a.BossPercent };
            slider.Value = Math.Clamp(v, slider.Minimum, slider.Maximum);
            value.Text = axis switch { 's' => $"{v:0.0}%", 'b' or 'p' => $"{v:0}%", _ => $"{v:+0;-0;0}" };
        }
        _syncing = false;
        _revert.IsEnabled = a.Changed;
    }

    /// <summary>Every live picture with the session's boxes (the 예시 pictures: <see cref="FitGuides"/>).</summary>
    void Redraw()
    {
        foreach (var (tab, pane) in _panes)
        {
            pane.Boxes.Children.Clear();
            if (_adj is { } a && pane.Rect.Width > 0) DrawParts(pane, a, tab, handle: true);
        }
    }

    /// <summary>Every box on one picture (the tab's own part drawn thicker): the bar's slots (and its corner handle), the buff and
    /// debuff rows split into their boxes and into icons over timers (<see cref="Geometry.RowGrid"/>), the boss row's slash.
    /// Line widths and labels keep their size on screen at any zoom.</summary>
    static void DrawParts(Pane pane, FitAdjust a, AdjustPart tab, bool handle)
    {
        double z = pane.Zoom;
        foreach (var (part, name, color) in Parts)
        {
            var brush = new SolidColorBrush(color);
            var r = a.RectOf(part);
            Draw(pane, r, brush, (part == tab ? 3 : 1) / z, name);
            var thin = new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B));
            switch (part)
            {
                case AdjustPart.스킬창:
                    foreach (var slot in SlotInfo.All) Draw(pane, Geometry.Slot(slot, a.Vp, a.Current), brush, 1 / z, null);
                    if (!handle) break;
                    double s = pane.Scale, hx = (r.X - pane.Rect.X) * s, hy = (r.Bottom - pane.Rect.Y) * s, h = Handle / z;
                    var corner = new Rectangle { Width = h, Height = h, Fill = brush };
                    Canvas.SetLeft(corner, hx);
                    Canvas.SetTop(corner, hy - h);
                    pane.Boxes.Children.Add(corner);
                    break;
                case AdjustPart.버프 or AdjustPart.디버프:
                    var boxes = part == AdjustPart.버프 ? Geometry.BuffBoxes(a.Vp, a.Current, a.BuffSize) : Geometry.DebuffBoxes(a.Vp, a.Current, a.BuffSize);
                    var (columns, iconBottom) = Geometry.RowGrid(boxes);
                    foreach (int x in columns) Line(pane, x, r.Y, x, r.Bottom, thin, 1 / z);
                    Line(pane, r.X, iconBottom, r.Right, iconBottom, thin, 1 / z);
                    break;
                case AdjustPart.보스:
                    double axis = r.X + Geometry.BossAxis * Geometry.BossScale(a.Vp, a.Current);
                    Line(pane, axis, r.Y, axis, r.Bottom, thin, 1 / z);
                    break;
            }
        }
    }

    /// <summary>A line between two screen points on <paramref name="pane"/>.</summary>
    static void Line(Pane pane, double x1, double y1, double x2, double y2, Brush brush, double thickness)
    {
        double s = pane.Scale;
        pane.Boxes.Children.Add(new Line
        {
            X1 = (x1 - pane.Rect.X) * s, Y1 = (y1 - pane.Rect.Y) * s, X2 = (x2 - pane.Rect.X) * s, Y2 = (y2 - pane.Rect.Y) * s,
            Stroke = brush, StrokeThickness = thickness,
        });
    }

    static void Draw(Pane pane, PixelRect r, Brush brush, double thickness, string? name)
    {
        if (r.Intersect(pane.Rect) is null) return;
        double s = pane.Scale, z = pane.Zoom;
        var box = new Rectangle { Width = r.Width * s, Height = r.Height * s, Stroke = brush, StrokeThickness = thickness };
        Canvas.SetLeft(box, (r.X - pane.Rect.X) * s);
        Canvas.SetTop(box, (r.Y - pane.Rect.Y) * s);
        pane.Boxes.Children.Add(box);
        if (name is null) return;
        var label = new TextBlock { Text = name, Foreground = brush, FontSize = 11 / z, Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)) };
        Canvas.SetLeft(label, (r.X - pane.Rect.X) * s);
        Canvas.SetTop(label, Math.Max(0, (r.Y - pane.Rect.Y) * s - 15 / z));
        pane.Boxes.Children.Add(label);
    }
}
