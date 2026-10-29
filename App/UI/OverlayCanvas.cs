using System.Windows;
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
    public const string PreviewNote = "다른 직업각인을 편집 중입니다. 그 직업각인의 칸 배치는 그 캐릭터로 접속해 바꿉니다. 창은 옮겨집니다.";

    /// <summary>Said when a box of a member drawn from a received layout is grabbed: it can't move.</summary>
    public const string ReceivedNote = "이 파티원은 받은 칸 배치로 그려집니다. 파티 탭의 파티원 배치 받기를 끄면 칸을 옮길 수 있습니다.";

    readonly Settings _s;
    readonly CharacterService _chars;
    readonly Canvas _world = new();
    readonly System.Windows.Controls.Image _shot = new() { Stretch = Stretch.Fill };   // placed by ShowPicture
    readonly OverlayRenderer _mine, _buffs, _debuffs, _judgment;
    readonly OverlayRenderer[] _peers;
    // ponytail: the primary screen stands for the game's; take the game window's monitor when a screenshot is linked (phase D)
    readonly Rect _screen = new(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    readonly Rectangle _grip = new() { Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Rectangle _pick = new() { Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = [3, 2], IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    (OverlayRenderer R, Part? Part)? _picked;
    int? _pickedDebuff;   // a debuff box picked instead
    EditorView? _v, _beforeFull;
    bool _showMine = true, _showParty = true;
    OverlayRenderer? _lastPlate;   // whose boxes were dragged last, which ResetBoxes resets

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

    public OverlayCanvas(Settings live, CharacterService chars)
    {
        (_s, _chars) = (live, chars);
        ClipToBounds = true;
        Focusable = true;   // arrow keys nudge the pick
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F));
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
        Play = new PreviewPlayer(_mine, _buffs, _debuffs, _judgment);
        Child = _world;
        ShowSample();

        SizeChanged += (_, _) =>
        {
            if (_v is not null || ActualWidth <= 0) return;
            _v = EditorView.Initial(ActualWidth, ActualHeight, _screen.Width, _screen.Height);
            ShowView();
        };
        MouseWheel += (_, e) => { var at = e.GetPosition(this); _v?.ZoomAt(at.X, at.Y, e.Delta > 0 ? ZoomStep : 1 / ZoomStep); ShowView(); ShowPick(); };
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += (_, _) => OnUp();
        LostMouseCapture += (_, _) => _drag = Drag.None;   // released, or taken away (Alt+Tab): no drag on hover
        KeyDown += OnKey;
        Unloaded += (_, _) => Play.Stop();   // a running ▶ timer would keep ticking (and the canvas alive)
    }

    public bool ShowMine { get => _showMine; set { _showMine = value; Relayout(); } }
    public bool ShowParty { get => _showParty; set { _showParty = value; Relayout(); } }
    public bool Snap { get => _s.Overlay.LayoutSnap; set => _s.Overlay.LayoutSnap = value; }
    public bool LinkSizes { get => _s.Overlay.LayoutLinkSizes; set => _s.Overlay.LayoutLinkSizes = value; }

    OverlayRenderer Renderer(int peer, bool buffs)
    {
        var r = new OverlayRenderer { Settings = _s, Peer = peer, BuffsOnly = buffs, ShowHidden = true };
        _world.Children.Add(r);
        return r;
    }

    PreviewTarget? _previewTarget;

    /// <summary>My overlay drawn as <paramref name="target"/> (편집 중's 직업각인; null: the character's), with its sample.</summary>
    public void SetTarget(PreviewTarget? target)
    {
        _previewTarget = target;
        _mine.SetTarget(target);
        ShowSample();
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

    /// <summary>The overlay went to <paramref name="profile"/>: its spots and screenshot (the host shows the picture).</summary>
    public void ShowProfile(int profile)
    {
        ReleaseMouseCapture();
        _s.SwitchProfile(profile);
        Relayout();
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

    /// <summary>Redraws every overlay from the settings and puts it at its spot.</summary>
    public void Relayout()
    {
        _s.SyncPlace();   // the class picked may have a spot of its own
        _mine.Visibility = Shown(_showMine);
        _buffs.Visibility = Shown(_showMine && _s.Buffs.Detached);
        _debuffs.Visibility = Shown(_showMine && _s.Debuffs.Raid != Raid.없음);
        _judgment.Visibility = Shown(_showMine && _s.Judgment.On(_s.ActiveCharacter));
        foreach (var p in _peers) p.Visibility = Shown(_showParty);
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
            case { Kind: "BuffBar" } b when _s.Buffs.Detached: Pick(_buffs, b.Part); break;
            case { Kind: not "Party" } b: Pick(_mine, b.Part); break;
        }
    }

    CanvasBox Box(OverlayRenderer r, Part? part) =>
        r == _judgment ? new("Judgment", part, null)
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
        _grip.Visibility = Visibility.Collapsed;
        (OverlayRenderer? On, Rect? Rect) shown = _pickedDebuff is { } d ? (_debuffs, _debuffs.DebuffRect(d))
            : _picked is ({ } r, { } part) ? (r, r.PartRect(part)) : (null, null);
        var (on, rect) = shown;
        bool visible = rect is not null && on?.Visibility == Visibility.Visible;   // not on a window turned off (디버프 창)
        _pick.Visibility = Shown(visible);
        if (!visible || rect is not { } b || on is null) return;
        (_pick.Width, _pick.Height) = (b.Width + 4, b.Height + 4);
        Canvas.SetLeft(_pick, Canvas.GetLeft(on) + b.X - 2);
        Canvas.SetTop(_pick, Canvas.GetTop(on) + b.Y - 2);
        if (on == _buffs || on == _judgment || on.ShowsReceivedLayout || Previewing(on)) return;
        double g = GripSize(b);
        (_grip.Width, _grip.Height, _grip.StrokeThickness) = (g, g, 1 / (_v?.Scale ?? 1));
        Canvas.SetLeft(_grip, Canvas.GetLeft(on) + b.Right - g);
        Canvas.SetTop(_grip, Canvas.GetTop(on) + b.Bottom - g);
        _grip.Visibility = Visibility.Visible;
    }

    // ── 기본 배치 ────────────────────────────────────────────────────────────────────

    /// <summary>기본 배치's label: whose boxes <see cref="ResetBoxes"/> resets, yours or a party member's class (기본 while it follows 기본).</summary>
    internal string ResetLabel
    {
        get
        {
            var r = _lastPlate ?? _mine;
            string cls = ClassProfiles.IsOwn(_s, r.ClassName) ? r.ClassName! : "기본";
            return r.Peer < 0 ? "기본 배치 (나)" : $"기본 배치 (파티: {cls})";
        }
    }

    /// <summary>The boxes of the plate dragged last (yours at first) back to the fixed layout.</summary>
    internal void ResetBoxes()
    {
        GestureStarted?.Invoke();
        Boxes(_lastPlate ?? _mine).Layout = null;
        Relayout();
        Changed?.Invoke();
    }

    /// <summary>The boxes <paramref name="r"/> draws: yours for the class shown, or a party member's for theirs; 기본's
    /// while that class follows 기본.</summary>
    ClassProfile Boxes(OverlayRenderer r) =>
        ClassProfiles.BoxesShown(_s, r.Peer < 0 ? ClassProfiles.ActiveKey(_s) : r.ClassName, member: r.Peer >= 0);
}
