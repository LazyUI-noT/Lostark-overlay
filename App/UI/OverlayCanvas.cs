using System.Windows;
using GaugeOverlay.App.Platform;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>What was picked on the canvas. <c>Kind</c>: "Skills", "BuffBar", "Gauge", "Judgment", "Debuff", "Party" or
/// "Plate" (a window's plate, not a box); <c>Debuff</c> is the debuff box's index.</summary>
public readonly record struct CanvasBox(string Kind, Part? Part, int? Debuff);

/// <summary>The game screen (a screenshot) with every overlay drawn where it stands. The wheel zooms around the cursor, a
/// drag on the picture (or a right or middle drag anywhere) pans; a box drags (by its corner: resizes) on its own, the
/// plate around the boxes moves its window, the plate's bottom-right corner scales it; arrow keys nudge what is picked
/// (Shift: 10 px). Every edit goes straight into the <see cref="Settings"/> it was given.</summary>
public sealed partial class OverlayCanvas : Border
{
    const double Grip = 10, ZoomStep = 1.15;
    const double Strip = OverlayWindow.StripHeight, Bleed = OverlayRenderer.Bleed;

    /// <summary>Said when a box of my window is grabbed while 편집 중 shows another 직업각인: it can't move.</summary>
    public const string PreviewNote = "다른 직업각인을 편집하는 중입니다. 칸 배치는 그 캐릭터로 접속해야 바꿀 수 있고, 창은 지금도 옮길 수 있습니다.";

    /// <summary>Said when a box of a member drawn from a received layout is grabbed: it can't move.</summary>
    public const string ReceivedNote = "이 파티원은 보내 준 칸 배치 그대로 그려집니다. 직접 옮기려면 파티 공유 → 보내기·받기에서 '배치 받기'를 끄세요.";

    readonly Settings _live;
    Settings _s;   // what the windows draw and drags write: the live settings or 편집 중's copy (SetEdit)
    readonly CharacterService _chars;
    readonly Canvas _world = new();
    readonly System.Windows.Controls.Image _shot = new() { Stretch = Stretch.Fill };   // placed by ShowPicture
    readonly OverlayRenderer _mine, _buffs, _gauge, _debuffs, _judgment, _amsu, _raid, _ring;
    readonly OverlayRenderer? _meter;   // the scene only (설정 → 기능 Map): the 미터기 has its own page
    readonly OverlayRenderer[] _peers;
    readonly bool _scene;
    // ponytail: the primary screen stands for the game's; take the game window's monitor when a screenshot is linked (phase D)
    readonly Rect _screen = new(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    readonly Rectangle _grip = new() { Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Rectangle _gripRight = Handle(), _gripBottom = Handle();   // a picked window's edge handles, where it has two sizes
    static Rectangle Handle() => new() { Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    // The picked box in the accent gold, as its chip in the inspector (user, 2026-11-12); the box under the pointer dashed white.
    readonly Rectangle _pick = new() { StrokeThickness = 3, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Rectangle _hover = new() { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = [3, 2], Opacity = 0.8, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    (OverlayRenderer R, Part? Part)? _picked;
    int? _pickedDebuff;   // a debuff box picked instead
    EditorView? _v, _beforeFull;
    bool _showMine = true, _showParty = true, _showOff = true;

    /// <summary>Something was picked (null: nothing), by a click or <see cref="Select"/>.</summary>
    public event Action<CanvasBox?>? Selected;

    /// <summary>A drag or nudge changed the settings.</summary>
    public event Action? Changed;

    /// <summary>Raised once per drag or nudge, before its first change: the moment to record an undo step.</summary>
    public event Action? GestureStarted;

    /// <summary>A line for the note under the canvas (<see cref="ReceivedNote"/>); null: that refusal is over.</summary>
    public event Action<string?>? Note;

    /// <summary>The ▶ preview player over the canvas's own boxes.</summary>
    internal PreviewPlayer Play { get; }

    /// <param name="scene">설정 → 기능 Map's read-only scene (OverlayCanvas.Scene.cs): <paramref name="live"/> is then a copy.</param>
    public OverlayCanvas(Settings live, CharacterService chars, bool scene = false)
    {
        (_s, _live, _chars, _scene) = (live, live, chars, scene);
        ClipToBounds = true;
        Focusable = true;   // arrow keys nudge the pick
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F));
        _world.Children.Add(_shot);
        _mine = Renderer(-1, buffs: false);
        _buffs = Renderer(-1, buffs: true);
        _gauge = Renderer(-1, buffs: false);
        _gauge.GaugeOnly = true;
        _debuffs = Renderer(-1, buffs: false);
        _debuffs.DebuffsOnly = true;
        _judgment = Renderer(-1, buffs: false);
        _judgment.JudgmentOnly = true;
        _amsu = Renderer(-1, buffs: false);
        _amsu.AmsuOnly = true;
        _raid = Renderer(-1, buffs: false);
        _raid.RaidOnly = true;
        _ring = Renderer(-1, buffs: false);
        _ring.RingOnly = true;
        _peers = [.. Enumerable.Range(0, _s.Party.ShowOthersBySlot.Count).Select(i => Renderer(i, buffs: false))];
        if (scene)
        {
            _meter = Renderer(-1, buffs: false);
            _meter.MeterOnly = true;
            _world.Children.Insert(0, _frame = ScreenFrame());   // under the screenshot and the windows
        }
        _pick.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        _world.Children.Add(_hover);
        _world.Children.Add(_pick);
        _world.Children.Add(_grip);
        _world.Children.Add(_gripRight);
        _world.Children.Add(_gripBottom);
        Play = _meter is null ? new PreviewPlayer(_mine, _buffs, _gauge, _debuffs, _judgment, _amsu, _raid, _ring) : new PreviewPlayer(_mine, _buffs, _gauge, _debuffs, _judgment, _amsu, _raid, _ring, _meter);
        if (scene)   // previews play at 초당 그리기 횟수, as the overlays do (user, 2026-11-10; was a fixed 15 fps)
        {
            double frameMs = DrawSteps.FrameMs(live.DrawsPerSecond, fadeOnly: false);
            foreach (var r in _world.Children.OfType<OverlayRenderer>()) r.MinFrameMs = frameMs;
            Play.Every = TimeSpan.FromMilliseconds(frameMs);
        }
        Child = _world;
        ShowSample();

        SizeChanged += (_, _) =>
        {
            if (_v is not null && !scene || ActualWidth <= 0) return;
            if (scene)   // the scene fits round its windows at every size
            {
                _v ??= EditorView.Whole(ActualWidth, ActualHeight, _screen.Width, _screen.Height);
                _sceneFitted = false;
                Relayout();
                return;
            }
            _v = EditorView.Initial(ActualWidth, ActualHeight, _screen.Width, _screen.Height);
            ShowView();
        };
        Unloaded += (_, _) => Play.Stop();   // a running ▶ timer would keep ticking (and the canvas alive)
        if (scene)
        {
            MouseDown += OnSceneDown;
            MouseMove += OnSceneMove;
            KeyDown += OnSceneKey;
            MouseLeave += (_, _) => SceneHover?.Invoke(null);
            return;
        }
        MouseWheel += (_, e) => { var at = e.GetPosition(this); _v?.ZoomAt(at.X, at.Y, e.Delta > 0 ? ZoomStep : 1 / ZoomStep); ShowView(); ShowPick(); ZoomFaint(); };
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseLeave += (_, _) => { ShowHover(null); Cursor = null; };
        MouseUp += (_, _) => OnUp();
        LostMouseCapture += (_, _) => _drag = Drag.None;   // released, or taken away (Alt+Tab): no drag on hover
        KeyDown += OnKey;
    }

    public bool ShowMine { get => _showMine; set { _showMine = value; Relayout(); } }
    public bool ShowParty { get => _showParty; set { _showParty = value; Relayout(); } }

    /// <summary>꺼진 창: windows that are off in 설정 are drawn faint (on), or not at all and not pickable (off). Not saved.</summary>
    public bool ShowOff { get => _showOff; set { _showOff = value; Relayout(); } }
    public bool Snap { get => _live.Overlay.LayoutSnap; set => _live.Overlay.LayoutSnap = value; }

    /// <summary>칸 하나씩: a drag, nudge or Ctrl+arrow on a 내 스킬창 box moves or sizes that box, not the window (<see cref="BoxDragRule"/>; not saved).</summary>
    public bool BoxesApart { get; set; }

    bool MovesBox(OverlayRenderer r) => BoxDragRule.MovesBox(r == _mine, BoxesApart);
    public bool LinkSizes { get => _live.Overlay.LayoutLinkSizes; set => _live.Overlay.LayoutLinkSizes = value; }

    OverlayRenderer Renderer(int peer, bool buffs)
    {
        var r = new OverlayRenderer { Settings = _s, Peer = peer, BuffsOnly = buffs, ShowHidden = true };
        r.SizeChanged += (_, _) => { if (_picked?.R == r || _pickedDebuff is not null && r == _debuffs) ShowPick(); };   // the gold frame follows the window's size
        _world.Children.Add(r);
        return r;
    }

    PreviewTarget? _previewTarget;

    /// <summary>The 기력·충격 창 is drawn only while 편집 중 (else the character) is a class with a ring (브레이커; user, 2026-11-14).</summary>
    bool RingShown => (_previewTarget?.Class ?? _chars.ActiveClass?.Name) is { } cls && _chars.HasRing(cls);

    /// <summary>My overlay drawn as <paramref name="target"/> (편집 중's 직업각인; null: the character's), with its sample.</summary>
    public void SetTarget(PreviewTarget? target)
    {
        _previewTarget = target;
        _mine.SetTarget(target);
        _gauge.SetTarget(target);   // the gauge window draws the pick's bar too
        ShowSample();
    }

    /// <summary>Draws and drags the windows of <paramref name="target"/>: 편집 중's copy (<see cref="Settings.EditTarget"/>,
    /// which shares the live 버프 막대 and party windows) or the live settings (edit-follows-pick.plan.md, phase 4).</summary>
    public void SetEdit(Settings target)
    {
        if (ReferenceEquals(target, _s)) return;
        ReleaseMouseCapture();   // a drag must not carry on into another 직업각인's spots
        _s = target;
        foreach (var r in _world.Children.OfType<OverlayRenderer>()) r.Settings = target;
        Relayout();
    }

    /// <summary>Every box in a state worth judging (some ready, some cooling), frozen; again after the class changes.</summary>
    public void ShowSample()
    {
        var (self, peers) = DemoSnapshots.Sample(Clock.Now());
        Play.Show(PreviewPlayer.SampleFor(_chars, _previewTarget));
        for (int i = 0; i < _peers.Length; i++) _peers[i].Update(peers[i % peers.Count], self.Snapshot.At);
    }

    void ShowView()
    {
        if (_v is { } v) _world.RenderTransform = new MatrixTransform(v.Scale, 0, 0, v.Scale, v.OffsetX, v.OffsetY);
    }

    /// <summary>원래 크기: the game screen at 100%, around the middle of the canvas.</summary>
    public void ActualSize()
    {
        _v?.ActualSize(ActualWidth / 2, ActualHeight / 2);
        ShowView();
    }

    /// <summary>전체 화면: the screen at 100% from the top-left (the host fills the monitor); off: the view before.</summary>
    public void FullScreen(bool on)
    {
        if (on) (_beforeFull, _v) = (_v, EditorView.FullScreen());
        else _v = _beforeFull ?? EditorView.Initial(ActualWidth, ActualHeight, _screen.Width, _screen.Height);   // none yet: the first view
        ShowView();
    }

    // ── where each overlay stands ──────────────────────────────────────────────────

    /// <summary>The window's spot (its top-left, the label strip included) on screen, the default one while unset.
    /// Bound party windows (파티 묶기) stand one under another from the first one's spot, their plates meeting.</summary>
    (double L, double T) Spot(OverlayRenderer r)
    {
        var area = Displays.WorkArea;
        static (double L, double T) Or((double L, double T) at, double left, double top) =>
            (double.IsNaN(left) ? at.L : left, double.IsNaN(top) ? at.T : top);
        var def = WindowDefaults.Place(area.Left, area.Width, area.Bottom, r.DesiredSize.Width, r.DesiredSize.Height + Strip, r.Peer, r.BuffsOnly || r.GaugeOnly || r.DebuffsOnly || r.JudgmentOnly || r.AmsuOnly || r.RaidOnly || r.RingOnly);
        if (r == _mine) return Or(def, _s.Overlay.Left, _s.Overlay.Top);
        if (r == _buffs) return Or(def, _s.Buffs.Left, _s.Buffs.Top);
        if (r == _gauge) return Or(GaugeDefault((def.Left + WindowDefaults.GaugeRight, def.Top)), _s.Gauge.Left, _s.Gauge.Top);
        if (r == _debuffs) return Or((def.Left + WindowDefaults.DebuffsRight, def.Top), _s.Debuffs.Left, _s.Debuffs.Top);   // as OverlayWindow.Replace
        if (r == _judgment) return Or((def.Left + WindowDefaults.JudgmentRight, def.Top), _s.Judgment.Left, _s.Judgment.Top);
        if (r == _amsu) return Or((def.Left + WindowDefaults.AmsuRight, def.Top), _s.Amsu.Left, _s.Amsu.Top);
        if (r == _ring) return Or((def.Left + WindowDefaults.GaugeRight, def.Top + WindowDefaults.RingBelow), _s.Ring.Left, _s.Ring.Top);   // as OverlayWindow.Replace
        if (r == _raid)   // as OverlayWindow.Replace
            return Or((area.Left + (area.Width - r.DesiredSize.Width) / 2 + WindowDefaults.RaidRight, area.Top + WindowDefaults.RaidTop), _s.Raids.Left, _s.Raids.Top);
        if (r == _meter)   // as OverlayWindow.Replace
            return Or((area.Left + area.Width - r.DesiredSize.Width - WindowDefaults.MeterFromRight,
                area.Bottom - WindowDefaults.AboveBottom - r.DesiredSize.Height - Strip - WindowDefaults.MeterAbove), _s.Meter.Left, _s.Meter.Top);
        if (!_s.Party.Together) return Or(def, _s.Party.ShowOthersBySlot[r.Peer].Left, _s.Party.ShowOthersBySlot[r.Peer].Top);
        if (r.Peer == 0) return Or(def, _s.Party.Left, _s.Party.Top);
        var above = _peers[r.Peer - 1];
        var (l, t) = Spot(above);
        return (l, t + PlateHeight(above));
    }

    /// <summary>Where the gauge window stands while never placed: where the plate drew the gauge (as OverlayWindow.PlaceAtHome), else <paramref name="fallback"/>.</summary>
    (double L, double T) GaugeDefault((double L, double T) fallback)
    {
        if (_mine.GaugeHome is not { } home) return fallback;
        var (l, t) = Spot(_mine);
        return (l + home.X - Bleed, t + home.Y - Bleed);
    }

    /// <summary>A gauge window never placed takes the spot and size the gauge was drawn at, and keeps them (as the game window does).
    /// Not while another 직업각인 is picked: the spot is the active character's, and the plate shows the pick's.</summary>
    void PlaceGaugeAtHome()
    {
        if (_previewTarget is not null || !_s.Apart || !double.IsNaN(_s.Gauge.Left) || _mine.GaugeHome is null || _mine.GaugeHomeScales is not { } scales) return;
        (_s.Gauge.Left, _s.Gauge.Top) = GaugeDefault((double.NaN, double.NaN));
        (_s.Gauge.Width, _s.Gauge.Height) = scales;
        _gauge.Refresh();
        _gauge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    }

    static double PlateHeight(OverlayRenderer r) => Math.Max(0, r.DesiredSize.Height - 2 * Bleed);

    void SetSpot(OverlayRenderer r, double left, double top)
    {
        if (r == _mine) (_s.Overlay.Left, _s.Overlay.Top) = (left, top);
        else if (r == _buffs) (_s.Buffs.Left, _s.Buffs.Top) = (left, top);
        else if (r == _gauge) (_s.Gauge.Left, _s.Gauge.Top) = (left, top);
        else if (r == _debuffs) (_s.Debuffs.Left, _s.Debuffs.Top) = (left, top);
        else if (r == _judgment) (_s.Judgment.Left, _s.Judgment.Top) = (left, top);
        else if (r == _amsu) (_s.Amsu.Left, _s.Amsu.Top) = (left, top);
        else if (r == _raid) (_s.Raids.Left, _s.Raids.Top) = (left, top);
        else if (r == _ring) (_s.Ring.Left, _s.Ring.Top) = (left, top);
        else if (_s.Party.Together)
        {
            // the stack moves as one: the first window by as much as this one is dragged
            var (l0, t0) = Spot(_peers[0]);
            var (l, t) = Spot(r);
            (_s.Party.Left, _s.Party.Top) = (l0 + left - l, t0 + top - t);
        }
        else (_s.Party.ShowOthersBySlot[r.Peer].Left, _s.Party.ShowOthersBySlot[r.Peer].Top) = (left, top);
    }

    /// <summary>Redraws every overlay from the settings and puts it at its spot.</summary>
    public void Relayout()
    {
        _mine.Visibility = Drawn(_mine, _showMine);
        _buffs.Visibility = Drawn(_buffs, _showMine && _s.Buffs.Detached);
        _gauge.Visibility = Drawn(_gauge, _showMine && _s.Apart);
        _debuffs.Visibility = Drawn(_debuffs, _showMine);
        _raid.Visibility = Drawn(_raid, _showMine);
        _judgment.Visibility = Drawn(_judgment, _showMine);
        _amsu.Visibility = Drawn(_amsu, _showMine);
        _ring.Visibility = Drawn(_ring, _showMine && RingShown);
        foreach (var p in _peers) p.Visibility = Drawn(p, _showParty);
        OverlayRenderer[] all = _meter is null ? [_mine, _buffs, _gauge, _debuffs, _judgment, _amsu, _raid, _ring, .. _peers] : [_mine, _buffs, _gauge, _debuffs, _judgment, _amsu, _raid, _ring, _meter, .. _peers];
        if (_scene) SceneVisibility(all);
        foreach (var r in all)
        {
            r.Refresh();
            r.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        PlaceGaugeAtHome();
        foreach (var r in all)
        {
            var (l, t) = Spot(r);
            Canvas.SetLeft(r, l - _screen.Left);
            Canvas.SetTop(r, t + Strip - _screen.Top);
        }
        if (_scene)
        {
            if (!_sceneFitted) FitScene(all);   // once (and on a resize): the looks cycling mustn't move the zoom
        }
        ShowFaint(all);
        if ((_pickedDebuff is not null ? _debuffs : _picked?.R) is { Visibility: not Visibility.Visible }) Pick(null, null);   // its window turned off or hidden
        ShowPick();
    }

    static Visibility Shown(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    // ── picking ────────────────────────────────────────────────────────────────────

    /// <summary>Picks <paramref name="box"/> as if clicked (null: nothing). A buff bar is picked in the buff window while it
    /// is detached; a party box is not picked from outside.</summary>
    public void Select(CanvasBox? box)
    {
        switch (box)
        {
            case null: Pick(null, null); break;
            case { Kind: "Debuff", Debuff: { } d }: PickDebuff(d); break;
            case { Kind: "Judgment" } b: Pick(_judgment, b.Part); break;
            case { Kind: "Amsu" } b: Pick(_amsu, b.Part); break;
            case { Kind: "Raid" } b: Pick(_raid, b.Part); break;
            case { Kind: "Ring" }: Pick(_ring, null); break;   // picked whole, as the gauge's own window
            case { Kind: "Party" } b when _peers.Length > 0: Pick(_peers[0], b.Part); break;
            case { Kind: "BuffBar" } b when _s.Buffs.Detached: Pick(_buffs, b.Part); break;
            case { Kind: "Gauge" } when _s.Apart: Pick(_gauge, null); break;   // its own window, picked whole
            case { Kind: not "Party" } b: Pick(_mine, b.Part); break;
            default: (_picked, _pickedDebuff) = (null, null); ShowPick(); break;   // party with no member drawn: no old gold frame left
        }
        // picked from the chips or search, it may lie off the view (e.g. 암수 at the screen's left): pan just enough to show it
        // (after layout: a pick while the page opens has no view or window size yet)
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if ((_pickedDebuff is not null ? _debuffs : _picked?.R) is not { Visibility: Visibility.Visible } shown || _v is not { } v) return;
            v.PanInto(Canvas.GetLeft(shown), Canvas.GetTop(shown), shown.ActualWidth, shown.ActualHeight, ActualWidth, ActualHeight, PickMargin);
            ShowView();
        });
    }

    const double PickMargin = 24;   // view px between a window panned into view and the canvas edge

    CanvasBox Box(OverlayRenderer r, Part? part) =>
        r == _gauge ? new("Gauge", Part.게이지, null)
        : r == _judgment ? new("Judgment", part, null)
        : r == _amsu ? new("Amsu", part, null)
        : r == _raid ? new("Raid", part, null)
        : r == _ring ? new("Ring", null, null)
        : r.Peer >= 0 ? new("Party", part, null)
        : part is null ? new("Plate", null, null)
        : part == Part.게이지 ? new("Gauge", part, null)
        : part >= FreeLayout.FirstBuff ? new("BuffBar", part, null)
        : new("Skills", part, null);

    /// <summary>Picks a box (null part: the plate) of <paramref name="r"/>; null renderer: nothing.</summary>
    void Pick(OverlayRenderer? r, Part? part)
    {
        (_picked, _pickedDebuff) = (r is null ? null : (r, part), null);
        ShowPick();
        Selected?.Invoke(r is null ? null : Box(r, part));
    }

    /// <summary>Picks a debuff box (null: nothing).</summary>
    void PickDebuff(int? i)
    {
        if (i is not { } d) { Pick(null, null); return; }
        (_picked, _pickedDebuff) = (null, d);
        ShowPick();
        Selected?.Invoke(new("Debuff", null, d));
    }

    /// <summary>Outlines the picked box; its corner grip, which resizes it, shows as a white square.</summary>
    void ShowPick()
    {
        _grip.Visibility = _gripRight.Visibility = _gripBottom.Visibility = Visibility.Collapsed;
        (OverlayRenderer? On, Rect? Rect) shown = _pickedDebuff is { } d ? (_debuffs, _debuffs.DebuffRect(d))
            : _picked is ({ } r, { } part) ? (r, r.PartRect(part))
            : _picked is ({ } w, null) ? (w, Whole(w)) : ((OverlayRenderer?)null, (Rect?)null);   // a window picked as a whole: its plate
        var (on, rect) = shown;
        if (!Outline(_pick, on, rect)) return;
        if (_picked is ({ } sized, null) && Sizing(sized) is { } sizing) { ShowPlateHandles(sized, Whole(sized), WindowSizing.ModeOf(_s, sizing) == SizeMode.Two); return; }
        if (rect is not { } b || on is null || _picked is (_, null)) return;   // a whole window has no box grip
        if (on == _buffs || on == _gauge || on == _judgment || on == _amsu || on == _raid || on == _ring || on.ShowsReceivedLayout || Previewing(on)) return;
        double g = GripSize(b);
        (_grip.Width, _grip.Height, _grip.StrokeThickness) = (g, g, 1 / (_v?.Scale ?? 1));
        Canvas.SetLeft(_grip, Canvas.GetLeft(on) + b.Right - g);
        Canvas.SetTop(_grip, Canvas.GetTop(on) + b.Bottom - g);
        _grip.Visibility = Visibility.Visible;
    }

    /// <summary>The white squares on a picked window's outline: the corner, and the middle of the right and bottom edges where it has two sizes.</summary>
    void ShowPlateHandles(OverlayRenderer on, Rect plate, bool edges)
    {
        double g = Math.Min(Grip / (_v?.Scale ?? 1), Math.Min(plate.Width, plate.Height) / 3);
        void Put(Rectangle h, double cx, double cy)
        {
            (h.Width, h.Height, h.StrokeThickness) = (g, g, 1 / (_v?.Scale ?? 1));
            Canvas.SetLeft(h, Canvas.GetLeft(on) + cx - g / 2);
            Canvas.SetTop(h, Canvas.GetTop(on) + cy - g / 2);
            h.Visibility = Visibility.Visible;
        }
        Put(_grip, plate.Right - g / 2, plate.Bottom - g / 2);
        if (!edges) return;
        Put(_gripRight, plate.Right, plate.Y + plate.Height / 2);
        Put(_gripBottom, plate.X + plate.Width / 2, plate.Bottom);
    }

    /// <summary>A window's plate, inside its bleed.</summary>
    static Rect Whole(OverlayRenderer r) => new(Bleed, Bleed, Math.Max(0, r.ActualWidth - 2 * Bleed), Math.Max(0, r.ActualHeight - 2 * Bleed));

    /// <summary>Puts <paramref name="frame"/> 2 px round <paramref name="rect"/> of <paramref name="on"/>; hidden with no rect or on
    /// a window turned off (디버프 창). Returns whether it shows.</summary>
    static bool Outline(Rectangle frame, OverlayRenderer? on, Rect? rect)
    {
        bool visible = rect is not null && on?.Visibility == Visibility.Visible;
        frame.Visibility = Shown(visible);
        if (!visible || rect is not { } b || on is null) return false;
        (frame.Width, frame.Height) = (b.Width + 4, b.Height + 4);
        Canvas.SetLeft(frame, Canvas.GetLeft(on) + b.X - 2);
        Canvas.SetTop(frame, Canvas.GetTop(on) + b.Y - 2);
        return true;
    }

    /// <summary>Outlines what a click at the pointer would pick (a box, else the window), with a hand: so the canvas reads as
    /// clickable (user, 2026-11-12). Nothing while ▶ runs.</summary>
    void ShowHover(System.Windows.Input.MouseEventArgs? e)
    {
        Rect? rect = null;
        var r = e?.OriginalSource as OverlayRenderer;
        if (r is not null && e is not null && !Play.Running)
        {
            var at = e.GetPosition(r);
            rect = r == _debuffs ? r.DebuffAt(at) is { } i ? r.DebuffRect(i) : Whole(r)
                : r.PartAt(at) is var (_, box) ? box : Whole(r);
        }
        Outline(_hover, r, rect);
    }

    /// <summary>The boxes <paramref name="r"/> draws: yours for the class shown, or a party member's for theirs; 기본's
    /// while that class follows 기본.</summary>
    ClassProfile Boxes(OverlayRenderer r) =>
        ClassProfiles.BoxesShown(_s, r.Peer < 0 ? ClassProfiles.ActiveKey(_s) : r.ClassName, member: r.Peer >= 0);
}
