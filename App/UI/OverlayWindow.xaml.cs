using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Net;
using Forms = System.Windows.Forms;

namespace GaugeOverlay.App.UI;

/// <summary>Transparent, topmost, click-through window hosting <see cref="OverlayRenderer"/>: yours, or one per party
/// slot, each with its own spot and size (<see cref="IWindowPlace"/>).
/// Edit mode makes it clickable: drag to move (8 px snapping to screen edges and centre lines, Shift disables),
/// the corner handle scales it, and sample data fills every box.</summary>
public partial class OverlayWindow : Window
{
    const double SnapDistance = 8;
    internal const double StripHeight = 20;   // label row, excluded from snapping (as is the renderer's bleed)
    const double Bleed = OverlayRenderer.Bleed;

    readonly Settings _settings;
    readonly int _peer;
    readonly bool _buffs;   // the buff bars' own window (버프 막대 따로 두기)
    readonly bool _gauge;   // the 아덴 게이지's own window (따로)
    readonly bool _debuffs;   // the debuff window (디버프)
    readonly bool _judgment;   // the 단심 window (설정 → 기타)
    readonly bool _amsu;   // the 암수 창
    readonly bool _raid;   // the 줄 수 창 (설정 → 레이드)
    readonly bool _meter;   // the DPS 미터기
    Player? _player, _shown;   // live data; what is drawn (sample data in edit mode)
    bool _editMode;
    enum Drag { None, Move, Scale }
    Drag _drag;
    System.Drawing.Point _dragFrom;
    double _startLeft, _startTop;
    (double W, double H) _startSize, _startPlate, _lastSize = (double.NaN, double.NaN);

    /// <summary>Raised when edit mode ends; position and scale are already written to the settings.</summary>
    public event Action? LayoutCommitted;

    /// <param name="peer">The party slot shown (0 = "party member 1"), or -1 for yourself.</param>
    /// <param name="buffs">Your buff bars alone, in a window of their own (버프 막대 따로 두기).</param>
    /// <param name="debuffs">The debuff window: your raid debuffs and the swap count (PLAN §4 디버프).</param>
    /// <param name="judgment">The 단심 window: 심판's 30 s cycle, or its icon while ready.</param>
    /// <param name="raid">The 줄 수 창: the boss bar count, glowing at the gate's alerts (설정 → 레이드).</param>
    public OverlayWindow(Settings settings, int peer = -1, bool buffs = false, bool debuffs = false, bool judgment = false, bool raid = false, bool meter = false, bool amsu = false, bool gauge = false)
    {
        _settings = settings;
        _peer = peer;
        InitializeComponent();
        (_buffs, _debuffs, _judgment, _raid, _meter, _amsu, _gauge) = (buffs, debuffs, judgment, raid, meter, amsu, gauge);
        if (peer >= 0) Title = $"GaugeOverlay - 파티원 {peer + 1}";
        if (buffs) Title = "GaugeOverlay - 버프";
        if (gauge) Title = "GaugeOverlay - 아덴 게이지";
        if (debuffs) Title = "GaugeOverlay - 디버프";
        if (judgment) Title = "GaugeOverlay - 단심";
        if (amsu) Title = "GaugeOverlay - 암수";
        if (raid) Title = "GaugeOverlay - 레이드";
        if (meter) Title = "GaugeOverlay - DPS 미터기";
        Renderer.Settings = settings;
        Renderer.Peer = peer;
        Renderer.BuffsOnly = buffs;
        Renderer.GaugeOnly = gauge;
        Renderer.DebuffsOnly = debuffs;
        Renderer.JudgmentOnly = judgment;
        Renderer.AmsuOnly = amsu;
        Renderer.RaidOnly = raid;
        Renderer.MeterOnly = meter;
        Loaded += (_, _) => PlaceInitially();

    }

    /// <summary>This window's spot and size. Looked up each time: a reset replaces the settings objects. A party
    /// member's window stands where its slot's does, at the size set for the class of whoever is shown in it.</summary>
    IWindowPlace Place => _gauge ? _settings.Gauge : _amsu ? _settings.Amsu : _meter ? _settings.Meter : _raid ? _settings.Raids : _judgment ? _settings.Judgment : _debuffs ? _settings.Debuffs : _buffs ? _settings.Buffs : _peer < 0 ? _settings.Overlay : new MemberPlace(_settings, _peer, ShownClass);

    /// <summary>The class of the member drawn here (sample data included); null while unknown or for yourself.</summary>
    string? ShownClass => _peer < 0 ? null : Emblems.Class(_shown?.Snapshot.ClassId ?? 0)?.Name;

    /// <summary>A member window's place: the spot is the slot's, the size the member profile's (yours with 내 설정 적용
    /// 크기). Resizing the window sets the profile's own size.</summary>
    /// With 파티 묶기 the first slot's window stands at the bound spot (the others stack under it, <see cref="PlateHeight"/>).
    sealed class MemberPlace(Settings s, int peer, string? cls) : IWindowPlace
    {
        MemberProfile Read => ClassProfiles.Of(s, cls).Members;
        bool Bound => s.Party.Together && peer == 0;
        PeerDisplay Spot => s.Party.ShowOthersBySlot[peer];
        public double Left { get => Bound ? s.Party.Left : Spot.Left; set { if (Bound) s.Party.Left = value; else Spot.Left = value; } }
        public double Top { get => Bound ? s.Party.Top : Spot.Top; set { if (Bound) s.Party.Top = value; else Spot.Top = value; } }
        public double Width { get => Read.UseMineSize ? s.Overlay.Width : Read.Width; set => Write().Width = value; }
        public double Height { get => Read.UseMineSize ? s.Overlay.Height : Read.Height; set => Write().Height = value; }

        MemberProfile Write()
        {
            var m = ClassProfiles.EditShown(s, cls).Members;
            if (m.UseMineSize) (m.UseMineSize, m.Width, m.Height) = (false, s.Overlay.Width, s.Overlay.Height);
            return m;
        }
    }

    /// <summary>The plate's height: with 파티 묶기 the next party window starts this far under this one, so the plates meet.</summary>
    public double PlateHeight => Math.Max(0, Renderer.ActualHeight - 2 * Bleed);

    /// <summary>A plate is being moved or scaled with the mouse.</summary>
    public bool Dragging => _drag != Drag.None;

    /// <summary>True while edit mode is on.</summary>
    public bool EditMode => _editMode;

    /// <summary>New live data: yourself, or the member now in this party slot (null = nobody). Ignored on screen while
    /// edit mode shows sample data, and drawn when it ends.</summary>
    public void Update(Player? player)
    {
        _player = player;
        if (_editMode) return;
        Renderer.Update(_shown = player);
    }

    /// <summary>Call after the settings object changed.</summary>
    public void Refresh()
    {
        // The boxes stay put on screen when the gauge above them changes thickness, or a box is dragged past the others'
        // edge: the window moves by the change of the renderer's anchor, so the gauge grows upwards. Not while the size
        // changes (that resizes everything from the top-left corner), nor between the fixed grid and a free layout (the
        // anchor is a different point in each, and the switch itself leaves the boxes where they are).
        var (before, free) = (Renderer.Anchor, Renderer.IsFree);
        var size = (Place.Width, Place.Height);
        Renderer.Refresh();
        Renderer.UpdateLayout();
        var after = Renderer.Anchor;
        double dx = after.X - before.X, dy = after.Y - before.Y;
        if (free == Renderer.IsFree && IsLoaded && size == _lastSize && !double.IsNaN(dx + dy) && (dx, dy) != (0, 0))
        {
            (Left, Top) = (Left - dx, Top - dy);
            (Place.Left, Place.Top) = (Left, Top);
            // Moved while it grew, a layered window gets a new blank surface and WPF sends it only what changed, leaving
            // the rest of the plate unpainted; a new (still transparent) background redraws the whole window.
            Background = new SolidColorBrush(Colors.Transparent);
        }
        _lastSize = size;
    }

    /// <summary>Instant switch, no animation. Leaving edit mode commits position and scale and restores click-through.</summary>
    public void SetEditMode(bool on)
    {
        if (on == _editMode) return;
        _editMode = on;
        EditChrome.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ShowLabel();
        SetClickThrough(!on);
        Redraw();
        if (on) return;
        _drag = Drag.None;
        ReleaseMouseCapture();
        Place.Left = Left;
        Place.Top = Top;
        // The timer font was only capped while resizing, so a shrink and grow restores it; now the cap sticks.
        // Never below MinTimerFont: at the smallest sizes the cap is under it, and committing that would hide the
        // timer for good. Party members' boxes have no timer.
        if (_peer < 0 && !_buffs && !_gauge && !_debuffs && !_judgment && !_raid && !_meter && !_amsu)
            _settings.Overlay.TimerFont = Math.Min(_settings.Overlay.TimerFont,
                Math.Max(OverlayRenderer.MinTimerFont, OverlayRenderer.MaxTimerFont(_settings.Overlay.Width, _settings.Overlay.Height)));
        LayoutCommitted?.Invoke();
    }

    void ShowLabel()
    {
        EditLabel.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;
        string who = _amsu ? " (암수)" : _meter ? " (DPS 미터기)" : _raid ? " (레이드 줄 수)" : _judgment ? " (단심)" : _debuffs ? " (디버프)" : _buffs ? " (버프 막대)" : _gauge ? " (아덴 게이지)" : _peer < 0 ? "" : $" (파티원 {PartySlots.Label(_peer, _settings.Party.MyNumber)} · {ShownClass ?? "기본"} 파티원 설정)";
        EditLabelText.Text = $"편집 중{who} · {KeyNames.Display(_settings.Hotkeys.EditMode)}로 끝내기 · Shift: 자석 끄기";
        EditLabel.ToolTip = EditLabelText.Text;
    }

    void Redraw()
    {
        Renderer.ShowHidden = _editMode;   // sample data: a box that hides on cooldown stays faint, to be seen and dragged
        if (_editMode)
        {
            var (self, peers) = DemoSnapshots.Sample(Clock.Now(), _player?.Snapshot.AdenSlots);
            var sample = _peer < 0 ? self : peers[_peer % peers.Count];
            Renderer.Update(_shown = sample, frozenAt: sample.Snapshot.At);
        }
        else Renderer.Update(_shown = _player);
        ShowLabel();   // names the class whose settings are edited
    }

    void PlaceInitially() => Replace();

    /// <summary>Moves the window to its spot in the settings (the default spot while unset) and redraws: after the
    /// settings were replaced from outside (레이아웃 편집기 적용, a profile switch).</summary>
    public void Replace()
    {
        var p = Place;
        var area = Displays.WorkArea;
        var (left, top) = WindowDefaults.Place(area.Left, area.Width, area.Bottom, ActualWidth, ActualHeight, _peer, _buffs || _gauge || _debuffs || _judgment || _raid || _meter || _amsu);
        if (_gauge) left += WindowDefaults.GaugeRight;
        if (_debuffs) left += WindowDefaults.DebuffsRight;
        if (_judgment) left += WindowDefaults.JudgmentRight;
        if (_amsu) left += WindowDefaults.AmsuRight;
        if (_meter) (left, top) = (area.Left + area.Width - ActualWidth - WindowDefaults.MeterFromRight,
            area.Bottom - WindowDefaults.AboveBottom - ActualHeight - WindowDefaults.MeterAbove);
        if (_raid) (left, top) = (area.Left + (area.Width - ActualWidth) / 2 + WindowDefaults.RaidRight, area.Top + WindowDefaults.RaidTop);
        Left = double.IsNaN(p.Left) ? left : p.Left;
        Top = double.IsNaN(p.Top) ? top : p.Top;
        _lastSize = (double.NaN, double.NaN);   // not a gauge change: don't shift the window
        Refresh();
    }

    /// <summary>The gauge window, never placed (NaN): stands where the gauge was drawn on <paramref name="plate"/>, at the size it had
    /// there, and keeps that as its own spot from now on. Nothing while the plate draws no gauge (the default spot serves).</summary>
    public void PlaceAtHome(OverlayWindow plate)
    {
        if (!_gauge || !double.IsNaN(Place.Left) || !plate.IsLoaded) return;
        plate.Renderer.UpdateLayout();
        if (plate.Renderer.GaugeHome is not { } home || plate.Renderer.GaugeHomeScales is not { } scales) return;
        var p = Place;
        (p.Left, p.Top) = (plate.Left + home.X - Bleed, plate.Top + home.Y - Bleed);
        (p.Width, p.Height) = scales;
        Replace();
    }

    /// <summary>표시 화면: this window onto <paramref name="target"/>. Twice: crossing to a monitor of another scale, the first
    /// move lands by the old scale.</summary>
    public void MoveInto(IReadOnlyList<DisplayArea> displays, DisplayArea target)
    {
        var p = Place;
        (p.Left, p.Top) = DisplayMap.Into(p.Left, p.Top, ActualWidth, ActualHeight, displays, target);
        Replace();
        Replace();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.Set(Hwnd, ExStyle.ToolWindow | ExStyle.NoActivate, true);
        SetClickThrough(!_editMode);
        ApplyCaptureExclusion();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // 파티 묶기: the stack moves with the first party window; the others follow it (App.StackParty).
        if (!_editMode || (_peer > 0 && _settings.Party.Together)) return;
        _drag = e.OriginalSource == ScaleHandle ? Drag.Scale : Drag.Move;
        _dragFrom = Forms.Cursor.Position;
        (_startLeft, _startTop) = (Left, Top);
        _startSize = (Place.Width, Place.Height);
        _startPlate = (Renderer.ActualWidth - 2 * Bleed, Renderer.ActualHeight - 2 * Bleed);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == Drag.None) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var p = Forms.Cursor.Position;
        double dx = (p.X - _dragFrom.X) / dpi.DpiScaleX, dy = (p.Y - _dragFrom.Y) / dpi.DpiScaleY;
        switch (_drag)
        {
            case Drag.Move:
                (Left, Top) = Snap(_startLeft + dx, _startTop + dy, dpi);
                break;
            case Drag.Scale:
                // Across changes the box width, down the box height.
                var place = Place;
                (place.Width, place.Height) = _gauge
                    ? WindowSizing.Resize(_settings, SizedWindow.Gauge, SizeHandle.Corner, _startSize, _startPlate, dx, dy)   // length × thickness, swapped when upright
                    : OverlayRenderer.Resized(_startSize, _startPlate, dx, dy);
                Refresh();   // records the new size, so the next Refresh doesn't mistake it for a gauge change
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _drag = Drag.None;
        ReleaseMouseCapture();
    }

    /// <summary>Snaps the plate (not the transparent margins) to the screen's edges and centre lines.</summary>
    (double, double) Snap(double left, double top, DpiScale dpi)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return (left, top);
        var b = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        double sl = b.Left / dpi.DpiScaleX, sr = b.Right / dpi.DpiScaleX, st = b.Top / dpi.DpiScaleY, sb = b.Bottom / dpi.DpiScaleY;
        double w = Renderer.ActualWidth - 2 * Bleed, h = Renderer.ActualHeight - 2 * Bleed;
        return (SnapAxis(left + Bleed, w, sl, sr) - Bleed, SnapAxis(top + StripHeight + Bleed, h, st, sb) - StripHeight - Bleed);
    }

    static double SnapAxis(double pos, double size, double min, double max)
    {
        foreach (var (edge, line) in new[] { (pos, min), (pos + size, max), (pos + size / 2, (min + max) / 2) })
            if (Math.Abs(edge - line) <= SnapDistance) return pos + line - edge;
        return pos;
    }

    nint Hwnd => new WindowInteropHelper(this).Handle;

    void SetClickThrough(bool on)
    {
        if (Hwnd != 0) WindowStyles.Set(Hwnd, ExStyle.ClickThrough, on);
    }

    /// <summary>Keeps the overlay out of screen capture (our own Desktop Duplication included) while the setting is on,
    /// so an overlay placed over the skill bar can't corrupt the readings.</summary>
    public void ApplyCaptureExclusion()
    {
        if (Hwnd != 0) WindowStyles.SetExcludeFromCapture(Hwnd, _settings.Overlay.ExcludeFromCapture);
    }

    /// <summary>Re-asserts the always-on-top z-order. A fullscreen game can end up drawn over an already-Topmost
    /// layered window, especially right after launch; call this whenever the overlay is (re)shown.</summary>
    public void BringToFront()
    {
        Topmost = false;
        Topmost = true;
    }
}
