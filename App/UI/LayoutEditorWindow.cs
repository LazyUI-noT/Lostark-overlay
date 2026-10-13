using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Forms = System.Windows.Forms;

namespace GaugeOverlay.App.UI;

/// <summary>레이아웃 편집기: a screenshot of the game with the overlays drawn where they stand on screen. The wheel zooms
/// around the cursor, a drag on the picture (or a right or middle drag anywhere) pans, 원래 크기 shows it at 100%; a plate drags to move its window, its bottom-right corner scales it,
/// and with 칸 옮기기 on a box, yours or a party member's (their class's party boxes), drags (or, by its corner, resizes) on its own. Everything happens on a copy of the settings:
/// 적용 hands the editor's part to the live settings (<see cref="Settings.ApplyLayoutFrom"/>), 닫기 drops it, asking first
/// when 적용 would change something.</summary>
public sealed partial class LayoutEditorWindow : Window
{
    const double Grip = 10, ZoomStep = 1.15;
    const double Strip = OverlayWindow.StripHeight, Bleed = OverlayRenderer.Bleed;
    const string PanelBrushKey = "EditorPanelBrush";   // the panels over the screenshot: opaque, in 설정's theme

    readonly Settings _live, _s;
    readonly Border _view = new() { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F)) };
    readonly Canvas _world = new();
    readonly System.Windows.Controls.Image _shot = new() { Stretch = Stretch.Fill };   // placed by ShowShot
    readonly OverlayRenderer _mine, _buffs, _debuffs, _judgment;
    readonly OverlayRenderer[] _peers;
    // ponytail: the primary screen stands for the game's; take the game window's monitor when a screenshot is linked (phase D)
    readonly Rect _screen = new(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new(0, 6, 0, 0) };
    readonly CharacterService _chars;   // over the copy: the class picked here, not the game's
    readonly SettingsWindow _forms;     // never shown: its rows (게이지, 칸 옵션) edit the copy
    readonly PreviewPlayer _play;
    readonly Rectangle _grip = new() { Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Rectangle _pick = new() { Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = [3, 2], IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    (OverlayRenderer R, Part? Part)? _picked;
    int? _pickedDebuff;   // a debuff box picked instead (PickDebuff)
    EditorView? _v;
    bool _showMine = true, _showParty = true, _moveBoxes;

    enum Drag { None, Pan, Move, Scale, Box, BoxSize, DebuffSize }
    Drag _drag;
    Point _from;
    OverlayRenderer? _target, _lastPlate;   // _lastPlate: whose boxes were dragged last, which 기본 배치 resets
    (double L, double T) _startSpot;
    (double W, double H) _startSize, _startPlate;
    Part _part;
    Dictionary<Part, Cell> _startCells = [];   // the fixed grid stays null until the box really moves
    Part? _still;                              // Box, BoxSize: a box the drag doesn't move, kept where it was on screen
    Point _stillAt;
    int _debuffIndex;                                       // DebuffSize: the box dragged
    (double W, double H) _startBox, _startDrawn;           // its size setting and drawn size when the drag began
    const string ReceivedNote = "이 파티원은 받은 칸 배치로 그려집니다. 파티 탭의 파티원 배치 받기를 끄면 칸을 옮길 수 있습니다.";

    /// <summary>Raised after 적용 wrote the editor's changes into the live settings.</summary>
    public event Action? Applied;

    /// <summary>Raised when an editor preference (the panels hidden) was written into the live settings, to be saved.</summary>
    public event Action? PrefsChanged;

    public LayoutEditorWindow(Settings live)
    {
        _live = live;
        _s = Copy(live);
        _chars = new CharacterService(_s, CharacterService.DefaultProfilesDir);
        _forms = new SettingsWindow(_s, _chars, quickTab: false);
        _forms.SettingsChanged += Relayout;
        Title = "GaugeOverlay 레이아웃 편집기";
        (Width, Height, MinWidth, MinHeight) = (1400, 820, 1100, 500);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;   // the box options take 420 px; the screenshot needs the rest
        Themes.Apply(this, live.Theme);   // as 설정, whose box options it hosts
        Resources[PanelBrushKey] = new SolidColorBrush(Themes.PanelColor(live.Theme));

        _world.Children.Add(_shot);
        _mine = Renderer(-1, buffs: false);
        _buffs = Renderer(-1, buffs: true);
        _debuffs = Renderer(-1, buffs: false);
        _debuffs.DebuffsOnly = true;
        _judgment = Renderer(-1, buffs: false);
        _judgment.JudgmentOnly = true;
        _peers = [.. Enumerable.Range(0, _s.Party.ShowOthersBySlot.Count).Select(i => Renderer(i, buffs: false))];
        _world.Children.Add(_pick);
        _world.Children.Add(_grip);
        _play = new PreviewPlayer(_mine, _buffs, _debuffs, _judgment);
        _view.Child = _world;
        ShowSample();
        ShowShot();

        Content = Layout(_view, Panel(), FullScreenButton());

        _view.SizeChanged += (_, _) =>
        {
            if (_v is not null || _view.ActualWidth <= 0) return;
            _v = EditorView.Initial(_view.ActualWidth, _view.ActualHeight, _screen.Width, _screen.Height);
            ShowView();
        };
        _view.MouseWheel += (_, e) => { var at = e.GetPosition(_view); _v?.ZoomAt(at.X, at.Y, e.Delta > 0 ? ZoomStep : 1 / ZoomStep); ShowView(); ShowPick(); };
        _view.MouseDown += OnDown;
        _view.MouseMove += OnMove;
        _view.MouseUp += (_, _) =>
        {
            if (_drag == Drag.DebuffSize) _forms.ShowDebuffBox(_debuffIndex);   // its sliders show the new size
            _view.ReleaseMouseCapture();
        };
        _view.LostMouseCapture += (_, _) => _drag = Drag.None;   // released, or taken away (Alt+Tab): no drag on hover
        Loaded += (_, _) => { ShowShot(); Relayout(); };   // the picture again, at the window's real DPI
        Closed += (_, _) => _play.Stop();   // a running ▶ timer would keep ticking (and this window alive)
    }

    bool _closeUnasked;

    /// <summary>The app is quitting, which can't be cancelled: close without asking about changes not applied.</summary>
    public void CloseUnasked()
    {
        _closeUnasked = true;
        Close();
    }

    void Apply()
    {
        _live.ApplyLayoutFrom(_s);
        Applied?.Invoke();
    }

    /// <summary>닫기 or the window's ✕ with changes not applied: 예 applies them, 아니요 drops them, 취소 keeps editing.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeUnasked || !_live.LayoutDiffers(_s)) return;
        var answer = MessageBox.Show(this, "적용하지 않은 변경이 있습니다. 적용하고 닫을까요?\n\n예: 적용하고 닫기\n아니요: 적용하지 않고 닫기\n취소: 계속 편집",
            Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.Yes) Apply();
        else if (answer != MessageBoxResult.No) e.Cancel = true;
    }

    static Settings Copy(Settings s) =>
        System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(s, SettingsStore.Json), SettingsStore.Json)!;

    OverlayRenderer Renderer(int peer, bool buffs)
    {
        var r = new OverlayRenderer { Settings = _s, Peer = peer, BuffsOnly = buffs, ShowHidden = true };
        _world.Children.Add(r);
        return r;
    }

    /// <summary>Every box in a state worth judging (some ready, some cooling), frozen.</summary>
    void ShowSample()
    {
        var (self, peers) = DemoSnapshots.Sample(Clock.Now());
        _play.Show(PreviewPlayer.SampleFor(_chars));
        for (int i = 0; i < _peers.Length; i++) _peers[i].Update(peers[i % peers.Count], self.Snapshot.At);
    }

    void ShowView()
    {
        if (_v is { } v) _world.RenderTransform = new MatrixTransform(v.Scale, 0, 0, v.Scale, v.OffsetX, v.OffsetY);
    }

    // ── where each overlay stands ──────────────────────────────────────────────────

    /// <summary>The window's spot (its top-left, the label strip included) on screen, the default one while unset.
    /// Bound party windows (파티 묶기) stand one under another from the first one's spot, their plates meeting.</summary>
    (double L, double T) Spot(OverlayRenderer r)
    {
        var area = SystemParameters.WorkArea;
        static (double L, double T) Or((double L, double T) at, double left, double top) =>
            (double.IsNaN(left) ? at.L : left, double.IsNaN(top) ? at.T : top);
        var def = WindowDefaults.Place(area.Left, area.Width, area.Bottom, r.DesiredSize.Width, r.DesiredSize.Height + Strip, r.Peer, r.BuffsOnly || r.DebuffsOnly || r.JudgmentOnly);
        if (r == _mine) return Or(def, _s.Overlay.Left, _s.Overlay.Top);
        if (r == _buffs) return Or(def, _s.Buffs.Left, _s.Buffs.Top);
        if (r == _debuffs) return Or((def.Left + WindowDefaults.DebuffsRight, def.Top), _s.Debuffs.Left, _s.Debuffs.Top);   // as OverlayWindow.Replace
        if (r == _judgment) return Or((def.Left + WindowDefaults.JudgmentRight, def.Top), _s.Judgment.Left, _s.Judgment.Top);
        if (!_s.Party.Together) return Or(def, _s.Party.ShowOthersBySlot[r.Peer].Left, _s.Party.ShowOthersBySlot[r.Peer].Top);
        if (r.Peer == 0) return Or(def, _s.Party.Left, _s.Party.Top);
        var above = _peers[r.Peer - 1];
        var (l, t) = Spot(above);
        return (l, t + PlateHeight(above));
    }

    static double PlateHeight(OverlayRenderer r) => Math.Max(0, r.DesiredSize.Height - 2 * Bleed);

    void SetSpot(OverlayRenderer r, double left, double top)
    {
        if (r == _mine) (_s.Overlay.Left, _s.Overlay.Top) = (left, top);
        else if (r == _buffs) (_s.Buffs.Left, _s.Buffs.Top) = (left, top);
        else if (r == _debuffs) (_s.Debuffs.Left, _s.Debuffs.Top) = (left, top);
        else if (r == _judgment) (_s.Judgment.Left, _s.Judgment.Top) = (left, top);
        else if (_s.Party.Together)
        {
            // the stack moves as one: the first window by as much as this one is dragged
            var (l0, t0) = Spot(_peers[0]);
            var (l, t) = Spot(r);
            (_s.Party.Left, _s.Party.Top) = (l0 + left - l, t0 + top - t);
        }
        else (_s.Party.ShowOthersBySlot[r.Peer].Left, _s.Party.ShowOthersBySlot[r.Peer].Top) = (left, top);
    }

    /// <summary>Redraws every overlay from the settings copy and puts it at its spot.</summary>
    void Relayout()
    {
        _s.SyncPlace();   // the class picked may have a spot of its own
        _mine.Visibility = _showMine ? Visibility.Visible : Visibility.Collapsed;
        _buffs.Visibility = _showMine && _s.Buffs.Detached ? Visibility.Visible : Visibility.Collapsed;
        _debuffs.Visibility = _showMine && _s.Debuffs.Raid != Raid.없음 ? Visibility.Visible : Visibility.Collapsed;
        _judgment.Visibility = _showMine && _s.Judgment.On(_s.ActiveCharacter) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var p in _peers) p.Visibility = _showParty ? Visibility.Visible : Visibility.Collapsed;
        OverlayRenderer[] all = [_mine, _buffs, _debuffs, _judgment, .. _peers];
        foreach (var r in all)
        {
            r.Refresh();
            r.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        foreach (var r in all)
        {
            var (l, t) = Spot(r);
            Canvas.SetLeft(r, l - _screen.Left);
            Canvas.SetTop(r, t + Strip - _screen.Top);
        }
        if (_pickedDebuff is not null && _debuffs.Visibility != Visibility.Visible) Pick(null, null);   // 디버프 창 turned off
        ShowPick();
        ShowResetTarget();
    }

    // ── dragging ───────────────────────────────────────────────────────────────────

    IWindowPlace SizeOf(OverlayRenderer r) => r == _buffs ? _s.Buffs : r == _debuffs ? _s.Debuffs : r == _judgment ? _s.Judgment : _s.Overlay;

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_note.Text == ReceivedNote) ShowShot();   // a new click: the refusal is over, back to the screenshot's note
        // a right or middle drag pans anywhere, a left one on the picture (not on an overlay)
        if (e.ChangedButton is MouseButton.Right or MouseButton.Middle || e.ChangedButton == MouseButton.Left && e.OriginalSource is not OverlayRenderer)
        {
            (_drag, _from) = (Drag.Pan, e.GetPosition(_view));
            _view.CaptureMouse();
            return;
        }
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not OverlayRenderer r) return;
        var at = e.GetPosition(r);
        if (r == _debuffs) PickDebuff(r.DebuffAt(at));
        else if (r.Peer < 0) Pick(r, r.PartAt(at)?.Part);
        if (_play.Running)   // ▶: a click uses the box instead of moving it
        {
            if (_picked?.Part is { } used) _play.Click(used, PreviewPlayer.Sections(_s, _chars));
            e.Handled = true;
            return;
        }
        _target = r;
        _from = e.GetPosition(_world);
        _startSpot = Spot(r);
        bool corner = at.X >= r.ActualWidth - Bleed - Grip && at.Y >= r.ActualHeight - Bleed - Grip;
        _drag = corner && r.Peer < 0 ? Drag.Scale : Drag.Move;   // a member's size is its class's (right panel)
        if (_moveBoxes && r.ShowsReceivedLayout)   // a drag would go into the class's layout, which isn't the one drawn
            _note.Text = ReceivedNote;
        else if (_moveBoxes && r == _debuffs)
        {
            // a debuff box grows by its corner; the rest of the window still drags it (the boxes stand in a fixed row)
            if (r.DebuffAt(at) is { } i && r.DebuffRect(i) is var box && InGrip(box, at))
            {
                (_drag, _debuffIndex) = (Drag.DebuffSize, i);
                _startBox = (_s.Debuffs.Box(i).Width, _s.Debuffs.Box(i).Height);
                _startDrawn = (box.Width, box.Height);
            }
        }
        else if (_moveBoxes && r != _buffs && r.PartAt(at) is var (part, rect))   // the buff window is the fixed grid
        {
            _drag = InGrip(rect, at) ? Drag.BoxSize : Drag.Box;
            (_part, _lastPlate) = (part, r);
            _startCells = new(Boxes(r).Layout ?? r.CellsAsDrawn());
            var moving = _drag == Drag.BoxSize && _s.Overlay.LayoutLinkSizes ? FreeLayout.Linked(part).Parts : [part];
            _still = r.DrawnParts.Where(q => !moving.Contains(q)).Cast<Part?>().FirstOrDefault();
            if (_still is { } p && OnCanvas(r, p) is { } at0) _stillAt = at0;
            else _still = null;
        }
        _startSize = (SizeOf(r).Width, SizeOf(r).Height);
        _startPlate = (r.ActualWidth - 2 * Bleed, r.ActualHeight - 2 * Bleed);
        _view.CaptureMouse();
        e.Handled = true;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (_drag == Drag.None)
        {
            _view.Cursor = OverGrip(e) ? Cursors.SizeNWSE : null;
            return;
        }
        if (_drag == Drag.Pan)
        {
            var at = e.GetPosition(_view);
            _v?.Pan(at.X - _from.X, at.Y - _from.Y);
            _from = at;
            ShowView();
            return;
        }
        if (_target is not { } r) return;
        var now = e.GetPosition(_world);
        double dx = now.X - _from.X, dy = now.Y - _from.Y;
        switch (_drag)
        {
            case Drag.Move:
                SetSpot(r, _startSpot.L + dx, _startSpot.T + dy);
                break;
            case Drag.DebuffSize:
                var sized = _s.Debuffs.Box(_debuffIndex);
                (sized.Width, sized.Height) = BarSizes.DebuffResized(_startBox, _startDrawn, dx, dy);
                break;
            case Drag.Scale:
                var place = SizeOf(r);
                (place.Width, place.Height) = OverlayRenderer.Resized(_startSize, _startPlate, dx, dy);
                break;
            default:
                var scale = r.CellScale;
                if (_drag == Drag.BoxSize && _part >= FreeLayout.FirstBuff && _startCells.TryGetValue(_part, out var cell))
                {
                    // The bar is drawn at its cell × 길이 × 두께: grow the cell so the drawn corner follows the pointer.
                    var bar = _s.Buffs.Of((BuffKind)(_part - FreeLayout.FirstBuff));
                    var (fx, fy) = BarSizes.DragFactors(cell, BarSizes.Length(bar), BarSizes.Thickness(bar));
                    scale = (scale.X * fx, scale.Y * fy);
                }
                Boxes(r).Layout = FreeLayout.Drag(_startCells, r.DrawnParts, _part, dx, dy, scale, resize: _drag == Drag.BoxSize,
                    snap: _s.Overlay.LayoutSnap && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift),
                    link: _s.Overlay.LayoutLinkSizes);
                Relayout();
                KeepStill(r);
                return;
        }
        Relayout();
    }

    /// <summary>The plate is drawn from its top-left box, so a box dragged above or left of the others would push them
    /// down or right; the window moves back by as much, as <see cref="OverlayWindow.Refresh"/> does with its anchor.</summary>
    void KeepStill(OverlayRenderer r)
    {
        if (_still is not { } p || OnCanvas(r, p) is not { } at) return;
        double dx = at.X - _stillAt.X, dy = at.Y - _stillAt.Y;
        if ((dx, dy) == (0, 0)) return;
        var (l, t) = Spot(r);
        SetSpot(r, l - dx, t - dy);
        Relayout();
    }

    /// <summary>A box's corner grip (its bottom-right square, which resizes it): <see cref="Grip"/> on screen at any zoom,
    /// at most half the box so the rest still moves it.</summary>
    double GripSize(Rect box) => Math.Min(Grip / (_v?.Scale ?? 1), Math.Min(box.Width, box.Height) / 2);

    bool InGrip(Rect box, Point at) => GripSize(box) is var g && at.X >= box.Right - g && at.Y >= box.Bottom - g && box.Contains(at);

    /// <summary>The pointer is on a box's corner grip that a drag would resize (칸 옮기기 on).</summary>
    bool OverGrip(MouseEventArgs e)
    {
        if (!_moveBoxes || _play.Running || e.OriginalSource is not OverlayRenderer r || r == _buffs || r == _judgment || r.ShowsReceivedLayout) return false;
        var at = e.GetPosition(r);
        if (r == _debuffs) return r.DebuffAt(at) is { } i && InGrip(r.DebuffRect(i), at);
        return r.PartAt(at) is var (_, rect) && InGrip(rect, at);
    }

    static Point? OnCanvas(OverlayRenderer r, Part p) =>
        r.PartRect(p) is { } rect ? new Point(Canvas.GetLeft(r) + rect.X, Canvas.GetTop(r) + rect.Y) : null;

    /// <summary>The boxes <paramref name="r"/> draws: yours for the class shown, or a party member's for theirs; 기본's
    /// while that class follows 기본.</summary>
    ClassProfile Boxes(OverlayRenderer r) =>
        ClassProfiles.BoxesShown(_s, r.Peer < 0 ? ClassProfiles.ActiveKey(_s) : r.ClassName, member: r.Peer >= 0);

    // ── the fixed panel (top right) ────────────────────────────────────────────────

    RadioButton[] _profileButtons = [];
    readonly Button _reset = new() { Padding = new(8, 2, 8, 2), ToolTip = "칸을 마지막으로 옮긴 판(처음엔 내 칸)의 칸 배치를 처음의 고정된 배치로 되돌립니다." };

    /// <summary>기본 배치's label names whose boxes it resets: yours, or a party member's class (기본 while it follows 기본).</summary>
    void ShowResetTarget()
    {
        var r = _lastPlate ?? _mine;
        string cls = ClassProfiles.IsOwn(_s, r.ClassName) ? r.ClassName! : "기본";
        _reset.Content = r.Peer < 0 ? "기본 배치 (나)" : $"기본 배치 (파티: {cls})";
    }

    /// <summary>The live overlay went to <paramref name="profile"/> (tray, hotkey): the editor goes there too.</summary>
    public void ShowProfile(int profile) => _profileButtons[profile].IsChecked = true;

    CheckBox? _debuffWindow;

    /// <summary>A raid was picked in the tray: the copy follows, so 닫기 doesn't take it for a change and 적용 doesn't undo it.</summary>
    public void ShowRaid(Raid raid)
    {
        _s.Debuffs.Raid = raid;
        if (_debuffWindow is not null) _debuffWindow.IsChecked = raid != Raid.없음;
        Relayout();
    }

    /// <summary>편집: what 적용 saves first (프로필 and its screenshot, the windows, 칸 배치), then 보기, which only changes
    /// this view.</summary>
    UIElement Panel()
    {
        var p = new StackPanel { Width = 250 };
        var apply = new Button { Content = "적용", Padding = new(14, 4, 14, 4), Margin = new(0, 0, 8, 0),
            ToolTip = "바꾼 위치, 크기, 모양을 오버레이에 적용하고 저장합니다." };
        Themes.Primary(apply);   // nothing here reaches the overlay until 적용
        apply.Click += (_, _) => Apply();
        var close = new Button { Content = "닫기", Padding = new(14, 4, 14, 4), ToolTip = "적용하지 않은 변경이 있으면 닫기 전에 묻습니다." };
        close.Click += (_, _) => Close();
        p.Children.Add(Line(apply, close));

        p.Children.Add(Heading("오버레이 프로필"));   // as the tray and 앱 단축키 name them
        var profiles = Line();
        _profileButtons = new RadioButton[OverlayProfile.Count];
        for (int i = 0; i < OverlayProfile.Count; i++)
        {
            int n = i;
            var b = new RadioButton { Content = $"{i + 1}", GroupName = "profile", IsChecked = i == _s.ActiveProfile, Margin = new(0, 0, 6, 0), MinWidth = 0 };
            b.Checked += (_, _) => { _view.ReleaseMouseCapture(); _s.SwitchProfile(n); _pending = null; ShowShot(); Relayout(); };
            profiles.Children.Add(_profileButtons[i] = b);
        }
        p.Children.Add(profiles);

        p.Children.Add(ShotRows());

        p.Children.Add(Heading("내 오버레이"));
        p.Children.Add(Line(ColorButton("판 색", () => _s.Overlay.PlateColor, v => _s.Overlay.PlateColor = v),
            // ponytail: G3 모르둠 is the only raid; turning it on picks that one. Remember the last raid when a second one exists.
            _debuffWindow = Check("디버프 창", _s.Debuffs.Raid != Raid.없음, v => _s.Debuffs.Raid = v ? Raid.모르둠 : Raid.없음),
            // 단심 is on per 직업각인: this turns it on or off for the character the editor shows.
            Check("단심 창", _s.Judgment.On(_s.ActiveCharacter), v => { if (_s.ActiveCharacter is { } key) _s.Judgment.Set(key, v); })));
        // The buff bars: on the plate or in a window of their own (설정 → 버프's 분리된 창), and which way they lie
        // (세로: upright side by side; on the plate, left of it).
        p.Children.Add(SubLabel("버프 막대"));
        p.Children.Add(Line(Radio("판에 함께", "buffs", !_s.Buffs.Detached, () => _s.Buffs.Detached = false),
            Radio("분리된 창", "buffs", _s.Buffs.Detached, () => _s.Buffs.Detached = true)));
        p.Children.Add(Line(Radio("가로", "buffAxis", !_s.Buffs.Side, () => _s.Buffs.Side = false),
            Radio("세로", "buffAxis", _s.Buffs.Side, () => _s.Buffs.Side = true)));

        p.Children.Add(Heading("파티원 창"));
        p.Children.Add(Line(Radio("묶기", "party", _s.Party.Together, () => _s.Party.Together = true),
            Radio("따로", "party", !_s.Party.Together, () => _s.Party.Together = false)));
        p.Children.Add(Line(ColorButton("판 색", () => _s.Party.PlateColor, v => _s.Party.PlateColor = v)));

        p.Children.Add(Heading("칸 배치"));
        p.Children.Add(Line(Check("칸 옮기기", _moveBoxes, v => _moveBoxes = v), Check("자석", _s.Overlay.LayoutSnap, v => _s.Overlay.LayoutSnap = v),
            Check("크기 묶기", _s.Overlay.LayoutLinkSizes, v => _s.Overlay.LayoutLinkSizes = v)));
        _reset.Click += (_, _) => { Boxes(_lastPlate ?? _mine).Layout = null; Relayout(); };
        p.Children.Add(Line(_reset));
        p.Children.Add(_note);

        // 보기: only this view changes, nothing is saved.
        p.Children.Add(Heading("보기"));
        p.Children.Add(RunButtons());
        p.Children.Add(Line(Check("나", _showMine, v => _showMine = v), Check("파티원", _showParty, v => _showParty = v)));
        var actual = new Button { Content = "원래 크기 (100%)", Padding = new(8, 2, 8, 2), ToolTip = "게임 화면과 같은 크기로 봅니다." };
        actual.Click += (_, _) => { _v?.ActualSize(_view.ActualWidth / 2, _view.ActualHeight / 2); ShowView(); };
        p.Children.Add(Line(actual));
        p.Children.Add(new TextBlock { Text = "휠: 확대·축소 · 그림 끌기 (또는 오른쪽 끌기): 화면 이동", Opacity = 0.6, Margin = new(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        return Card("편집", p, HorizontalAlignment.Right);
    }

    Action<bool>? _setFull;

    /// <summary>--demo --pick: your <paramref name="part"/> as if clicked.</summary>
    internal void DemoPick(Part part) => Pick(_mine, part);

    /// <summary>--demo --full: 전체 화면 as if clicked, once the window is up.</summary>
    internal void DemoFullScreen() => Dispatcher.BeginInvoke(() => _setFull?.Invoke(true), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    /// <summary>전체 화면 (bottom right): the editor fills the screen with no frame, as the game does, and the screenshot
    /// fills it at 100%, every overlay exactly where the game draws it; again or Esc goes back to the view before.</summary>
    Button FullScreenButton()
    {
        var b = new Button { Content = "전체 화면", Padding = new(12, 4, 12, 4), Margin = new(12), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var before = WindowState;
        EditorView? view = null;   // the view to go back to
        void Set(bool full)
        {
            FullScreenLayout();
            if (full)
            {
                (view, _v) = (_v, EditorView.FullScreen());
                ShowView();
                // Already maximized, a window keeps its work-area size when the frame goes: restore, then maximize again.
                before = WindowState;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                (WindowStyle, WindowState) = (WindowStyle.SingleBorderWindow, before);
                _v = view ?? EditorView.Initial(_view.ActualWidth, _view.ActualHeight, _screen.Width, _screen.Height);   // none yet: the first view
                ShowView();
            }
            b.Content = full ? "전체 화면 끝내기 (Esc)" : "전체 화면";
        }
        b.Click += (_, _) => Set(WindowStyle != WindowStyle.None);
        _setFull = Set;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && WindowStyle == WindowStyle.None) Set(false); };
        return b;
    }

    static StackPanel Line(params UIElement[] items)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 2, 0, 2) };
        foreach (var i in items) s.Children.Add(i);
        return s;
    }

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 10, 0, 2) };

    /// <summary>What the rows under it set, within a heading's group.</summary>
    static TextBlock SubLabel(string text) => new() { Text = text, Opacity = 0.75, Margin = new(0, 6, 0, 0) };

    CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Margin = new(0, 0, 10, 0), MinWidth = 0 };
        c.Click += (_, _) => { set(c.IsChecked == true); Relayout(); };
        return c;
    }

    RadioButton Radio(string text, string group, bool value, Action set)
    {
        var b = new RadioButton { Content = text, GroupName = group, IsChecked = value, Margin = new(0, 0, 12, 0), MinWidth = 0 };
        b.Checked += (_, _) => { set(); Relayout(); };
        return b;
    }

    /// <summary>A colour from the settings file; a malformed one shows grey rather than failing.</summary>
    static Color Hex(string s)
    {
        try { return (Color)ColorConverter.ConvertFromString(s); }
        catch (FormatException) { return Colors.Gray; }
    }

    /// <summary>A swatch button that picks a colour (#RRGGBB) with the Windows colour dialog.</summary>
    Button ColorButton(string text, Func<string> get, Action<string> set)
    {
        var swatch = new Border { Width = 14, Height = 14, CornerRadius = new(2), Margin = new(0, 0, 6, 0), BorderThickness = new(1), BorderBrush = Brushes.Gray };
        void Show() => swatch.Background = new SolidColorBrush(Hex(get()));
        Show();
        var b = new Button { Content = Line(swatch, new TextBlock { Text = text }), Padding = new(6, 2, 6, 2), Margin = new(0, 0, 8, 0) };
        b.Click += (_, _) =>
        {
            var c = Hex(get());
            using var d = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
            if (d.ShowDialog() != Forms.DialogResult.OK) return;
            set($"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}");
            Show();
            Relayout();
        };
        return b;
    }
}
