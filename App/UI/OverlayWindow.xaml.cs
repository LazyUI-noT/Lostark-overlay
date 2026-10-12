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
    readonly bool _debuffs;   // the debuff window (디버프)
    Player? _player, _shown;   // live data; what is drawn (sample data in edit mode and preview)
    bool _editMode, _preview;
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
    public OverlayWindow(Settings settings, int peer = -1, bool buffs = false, bool debuffs = false)
    {
        _settings = settings;
        _peer = peer;
        InitializeComponent();
        (_buffs, _debuffs) = (buffs, debuffs);
        if (peer >= 0) Title = $"GaugeOverlay - 파티원 {peer + 1}";
        if (buffs) Title = "GaugeOverlay - 버프";
        if (debuffs) Title = "GaugeOverlay - 디버프";
        Renderer.Settings = settings;
        Renderer.Peer = peer;
        Renderer.BuffsOnly = buffs;
        Renderer.DebuffsOnly = debuffs;
        Loaded += (_, _) => PlaceInitially();

    }

    /// <summary>This window's spot and size. Looked up each time: a reset replaces the settings objects. A party
    /// member's window stands where its slot's does, at the size set for the class of whoever is shown in it.</summary>
    IWindowPlace Place => _debuffs ? _settings.Debuffs : _buffs ? _settings.Buffs : _peer < 0 ? _settings.Overlay : new MemberPlace(_settings, _peer, ShownClass);

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

    /// <summary>While 설정 is open and 오버레이 표시 is on: shown whatever is in front, with live data, so changed colours can be judged on it;
    /// sample data only when there is nothing live (no game, an empty party slot). Never clickable.</summary>
    public bool Preview
    {
        get => _preview;
        set { _preview = value; ShowLabel(); Redraw(); }
    }

    /// <summary>New live data: yourself, or the member now in this party slot (null = nobody). Ignored on screen while
    /// sample data is shown, and drawn when it ends.</summary>
    public void Update(Player? player)
    {
        bool wasSample = Sample;
        _player = player;
        if (_editMode) return;
        if (Sample || wasSample) Redraw(); else Renderer.Update(_shown = player);
    }

    /// <summary>Sample data is drawn: in edit mode, and while 설정 is open with nothing live or with another 직업각인 picked.</summary>
    bool Sample => _editMode || (_preview && (_player is null || _target is not null));

    PreviewTarget? _target;

    /// <summary>설정 has another 직업각인 than the character's picked in 편집 중 (<see cref="PreviewTarget.Decide"/>): this window,
    /// yours only, shows that 직업각인's sample with its settings and <paramref name="icons"/> (its saved ones) until cleared
    /// (null), which brings back the live data and the character's own icons.</summary>
    public void SetPreviewTarget(PreviewTarget? target, IReadOnlyDictionary<SlotId, Image>? icons = null)
    {
        if (_peer >= 0 || _buffs || _debuffs) return;
        _target = target;
        Renderer.SetTarget(target, icons);
        ShowLabel();
        Redraw();
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
        if (_peer < 0 && !_buffs && !_debuffs)
            _settings.Overlay.TimerFont = Math.Min(_settings.Overlay.TimerFont,
                Math.Max(OverlayRenderer.MinTimerFont, OverlayRenderer.MaxTimerFont(_settings.Overlay.Width, _settings.Overlay.Height)));
        LayoutCommitted?.Invoke();
    }

    void ShowLabel()
    {
        EditLabel.Visibility = Sample ? Visibility.Visible : Visibility.Collapsed;
        string who = _debuffs ? " (디버프)" : _buffs ? " (버프 막대)" : _peer < 0 ? "" : $" (파티원 {PartySlots.Label(_peer, _settings.Party.MyNumber)} · {ShownClass ?? "기본"} 파티원 설정)";
        EditLabelText.Text = _editMode
            ? $"편집 중{who} · {KeyNames.Display(_settings.Hotkeys.EditMode)}로 끝내기 · Shift: 자석 끄기"
            : _target is { } t ? $"미리보기 · {t.Key} (예시 데이터)"
            : $"미리보기{who} (예시 데이터)";
        EditLabel.ToolTip = EditLabelText.Text;
    }

    void Redraw()
    {
        Renderer.ShowHidden = Sample;   // sample data: a box that hides on cooldown stays faint, to be seen and dragged
        if (Sample)
        {
            var (self, peers) = DemoSnapshots.Sample(Clock.Now(), _target?.AdenSlots ?? _player?.Snapshot.AdenSlots);
            if (_target is { } t) self = self with { Snapshot = self.Snapshot with { ClassId = t.ClassId } };
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
        var area = SystemParameters.WorkArea;
        var (left, top) = WindowDefaults.Place(area.Left, area.Width, area.Bottom, ActualWidth, ActualHeight, _peer, _buffs || _debuffs);
        if (_debuffs) left += WindowDefaults.DebuffsRight;
        Left = double.IsNaN(p.Left) ? left : p.Left;
        Top = double.IsNaN(p.Top) ? top : p.Top;
        _lastSize = (double.NaN, double.NaN);   // not a gauge change: don't shift the window
        Refresh();
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
                (place.Width, place.Height) = OverlayRenderer.Resized(_startSize, _startPlate, dx, dy);
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
