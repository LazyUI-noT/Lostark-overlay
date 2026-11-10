using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>위치 조정: a borderless layer exactly over the game area (or 테스트 모드's video) that draws every read box at its true
/// screen position. Boxes are dragged, nudged with the arrow keys (Shift: 10 px), sized with their handle, + / − or Alt+↑/↓; Tab picks the
/// next part, Ctrl+Z goes back to where it opened, Enter applies, Esc closes without applying. A part that reads well now turns
/// green (<see cref="AdjustReads"/>). It is kept out of every capture, so the reads never see it. The edit is <see cref="FitAdjust"/>,
/// live in the reads while open (<see cref="AdjustChanged"/>); only 적용 keeps it.</summary>
public sealed partial class AdjustLayer : Window
{
    /// <summary>The edit to preview, for session gen.</summary>
    public event Action<int, FitAdjust>? AdjustChanged;
    /// <summary>적용: the edit, and whether it is 테스트 모드's (then nothing is saved).</summary>
    public event Action<FitAdjust, bool>? ApplyRequested;
    /// <summary>Closed without 적용 (Esc, 닫기): back to the saved fit.</summary>
    public event Action? Dismissed;

    /// <summary>--demo: the layer may be captured (screenshots of it); otherwise it is left out of every capture.</summary>
    public bool Capturable { get; set; }

    const double HandlePx = 12;     // screen px around a size handle that take it
    const int BigStep = 10;         // Shift + arrow

    FitAdjust? _adj;
    AdjustReads? _reads;
    int _gen;
    bool _test;
    AdjustPart _selected = AdjustPart.스킬창;
    (FitAdjust Start, AdjustPart Part, bool Corner, Point At)? _drag;
    readonly Canvas _boxes = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };   // alpha 1: takes the mouse

    public AdjustLayer()
    {
        (WindowStyle, AllowsTransparency, Background, Topmost, ShowInTaskbar, ResizeMode, Title) =
            (WindowStyle.None, true, Brushes.Transparent, true, false, ResizeMode.NoResize, "GaugeOverlay 위치 조정");
        var root = new Grid();
        root.Children.Add(_boxes);
        root.Children.Add(BuildToolbar());
        Content = root;
        _boxes.MouseLeftButtonDown += OnPress;
        _boxes.MouseMove += OnDrag;
        _boxes.MouseLeftButtonUp += (_, _) => _boxes.ReleaseMouseCapture();
        _boxes.LostMouseCapture += (_, _) => _drag = null;   // also Alt+Tab or a dialog mid-drag
        PreviewKeyDown += OnKey;
        Activated += (_, _) => ShowFocus();
        Deactivated += (_, _) => ShowFocus();
        SourceInitialized += (_, _) => WindowStyles.SetExcludeFromCapture(new WindowInteropHelper(this).Handle, !Capturable);
    }

    /// <summary>A preview session began (opened, or the game area or fit changed): the layer goes over <paramref name="vp"/> and its
    /// boxes start on <paramref name="fit"/>.</summary>
    public void Open(int gen, Viewport vp, Fit fit, int buffSize, bool test, int buffCount, int bossSize, PixelRect? panel)
    {
        (_gen, _test, _drag, _reads, _toolbarDrag) = (gen, test, null, null, null);
        _adj = FitAdjust.Begin(vp, buffSize, fit, buffCount, bossSize, panel);
        if (!IsVisible)
        {
            _result.Text = "";   // a re-open after 적용 keeps its result
            Show();
        }
        Place();
        Activate();
        ShowFocus();
        Redraw();
        AdjustChanged?.Invoke(_gen, _adj);   // the 전분 box to look at, and a start outside the game area clamped
    }

    /// <summary>The session's read states, ~5 a second.</summary>
    public void ShowReads(int gen, AdjustReads reads)
    {
        if (gen != _gen || _adj is null || reads == _reads) return;
        _reads = reads;
        Redraw();
    }

    /// <summary>적용 was kept: what was saved.</summary>
    public void Committed(string text) => _result.Text = text;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;   // made once, shown per session
        Dismiss();
    }

    /// <summary>Closes without 적용 (Esc, 닫기, or App when the game is gone).</summary>
    public void Dismiss()
    {
        if (!IsVisible) return;
        (_adj, _drag, _toolbarDrag) = (null, null, null);
        Hide();
        Dismissed?.Invoke();
    }

    void Apply()
    {
        if (_adj is { } a) ApplyRequested?.Invoke(a, _test);
    }

    void Changed(FitAdjust next)
    {
        _adj = next;
        Redraw();
        AdjustChanged?.Invoke(_gen, next);
    }

    double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Exactly over the game area in screen pixels. Twice: the first move may cross onto a monitor of another DPI, whose
    /// WM_DPICHANGED resizes the window by the DPI ratio; the second puts the size back.</summary>
    void Place()
    {
        if (_adj is not { } a) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var r = new PixelRect(a.Vp.X, a.Vp.Y, a.Vp.Width, a.Vp.Height);
        WindowStyles.PlaceInPixels(hwnd, r);
        WindowStyles.PlaceInPixels(hwnd, r);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(() => { Place(); Redraw(); });   // after WPF has applied Windows' suggested rect
    }

    /// <summary>A point on the layer as a screen pixel.</summary>
    (double X, double Y) ToScreen(Point p) => (_adj!.Vp.X + p.X * Dpi, _adj.Vp.Y + p.Y * Dpi);

    void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (_adj is not { } a) return;
        var at = e.GetPosition(_boxes);
        var (x, y) = ToScreen(at);
        if (a.PartAt(x, y, HandlePx) is not { } hit) return;
        _selected = hit.Part;
        _drag = (a, hit.Part, hit.Corner, at);
        _boxes.CaptureMouse();
        Redraw();
    }

    void OnDrag(object sender, MouseEventArgs e)
    {
        if (_drag is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(_boxes);
        double dx = (at.X - d.At.X) * Dpi, dy = (at.Y - d.At.Y) * Dpi;   // the whole distance from the press, so rounding never drifts
        Changed(d.Corner ? d.Start.DragCorner(d.Part, dx) : d.Start.Move(d.Part, dx, dy));
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (_adj is not { } a) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? BigStep : 1;
        // Alt + a key arrives as Key.System: Alt+↑/↓ sizes the picked part (Shift: 10).
        if (e.Key == Key.System && e.SystemKey is Key.Up or Key.Down)
        {
            Changed(a.Resize(_selected, e.SystemKey == Key.Up ? step : -step));
            e.Handled = true;
            return;
        }
        FitAdjust? next = e.Key switch
        {
            Key.Left => a.Move(_selected, -step, 0),
            Key.Right => a.Move(_selected, step, 0),
            Key.Up => a.Move(_selected, 0, -step),
            Key.Down => a.Move(_selected, 0, step),
            Key.OemPlus or Key.Add => a.Resize(_selected, 1),
            Key.OemMinus or Key.Subtract => a.Resize(_selected, -1),
            Key.Z when ctrl => a.Reverted(),
            _ => null,
        };
        if (next is not null) Changed(next);
        else if (e.Key == Key.Tab)
        {
            var parts = Enum.GetValues<AdjustPart>();
            _selected = parts[(Array.IndexOf(parts, _selected) + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? parts.Length - 1 : 1)) % parts.Length];
            Redraw();
        }
        else if (e.Key == Key.Enter) Apply();
        else if (e.Key == Key.Escape) Dismiss();
        else return;
        e.Handled = true;
    }
}
