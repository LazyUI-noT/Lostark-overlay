using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>읽는 자리's zoom window (reading-place.plan.md C): one box and the picture around it, enlarged. It hosts 설정's own
/// screen canvas, so the drawing, the drag, the size dot and the arrow keys are the page's (SettingsWindow.DetectPlace.cs);
/// this window adds the zoom (wheel, + / −, 1×–8×, starting fitted), scrollbars to pan, and the buttons. Enter 적용, Esc 취소.</summary>
sealed class PlaceWindow : Window
{
    const double MinZoom = 1, MaxZoom = 8, ZoomStep = 1.25;

    readonly Viewbox _view = new() { Stretch = System.Windows.Media.Stretch.Uniform };
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, Tokens.Space.Gap) };
    readonly TextBlock _zoomText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center };
    readonly Button _back = new() { Content = "되돌리기", Margin = new(0, 0, Tokens.Space.Gap, 0) };
    readonly Button _reset = new() { Content = "처음으로", Margin = new(0, 0, Tokens.Space.Gap, 0) };
    readonly Button _apply = new() { Content = "적용", Margin = new(0, 0, Tokens.Space.Gap, 0) };
    readonly FrameworkElement _screen;
    readonly CheckBox _sample = new() { Content = "예시 보기", VerticalAlignment = VerticalAlignment.Center, Margin = new(Tokens.Space.Card, 0, 0, 0),
        ToolTip = "게임 화면 대신 예시 그림을 보여 줍니다. 칸을 그림 속 아이콘에 맞춰 보세요." };
    double _zoom = MinZoom;

    /// <summary>적용 (also Enter) and 취소 (also Esc, closing the window); 되돌리기 (also Ctrl+Z) and 처음으로.</summary>
    public event Action? ApplyRequested, BackRequested, ResetRequested;

    /// <summary>예시 보기 turned on or off: the page draws the sample picture in place of the capture.</summary>
    public event Action<bool>? SampleToggled;

    /// <param name="hasSample">The box has a 예시 picture: only then is 예시 보기 offered.</param>
    /// <param name="option">A setting for the open box shown beside the zoom (스킬창과 함께 움직임), or null.</param>
    public PlaceWindow(Window owner, UiTheme theme, FrameworkElement screen, string title, bool movable, bool hasSample, FrameworkElement? option = null)
    {
        (Owner, Title, Width, Height, WindowStartupLocation, ShowInTaskbar) = (owner, title, 1000, 700, WindowStartupLocation.CenterOwner, false);
        _screen = screen;
        _view.Child = screen;
        _scroll.Content = _view;
        var minus = new Button { Content = "−", MinWidth = 36, ToolTip = "작게 (−)" };
        var plus = new Button { Content = "+", MinWidth = 36, ToolTip = "크게 (+)" };
        System.Windows.Automation.AutomationProperties.SetName(minus, "작게");   // read aloud instead of "−"
        System.Windows.Automation.AutomationProperties.SetName(plus, "크게");
        var cancel = new Button { Content = "취소" };
        minus.Click += (_, _) => Zoom(1 / ZoomStep);
        plus.Click += (_, _) => Zoom(ZoomStep);
        _back.Click += (_, _) => BackRequested?.Invoke();
        _reset.Click += (_, _) => ResetRequested?.Invoke();
        _apply.Click += (_, _) => ApplyRequested?.Invoke();
        cancel.Click += (_, _) => Close();
        Themes.Primary(_apply);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, Children = { minus, _zoomText, plus } };
        if (movable && hasSample)
        {
            _sample.Click += (_, _) => SampleToggled?.Invoke(_sample.IsChecked == true);
            zoom.Children.Add(_sample);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (movable) foreach (var b in new[] { _back, _reset, _apply }) buttons.Children.Add(b);
        buttons.Children.Add(cancel);
        var bar = new DockPanel { Margin = new(Tokens.Space.Card, Tokens.Space.Gap, Tokens.Space.Card, Tokens.Space.Card) };
        DockPanel.SetDock(zoom, Dock.Left);
        bar.Children.Add(zoom);
        if (option is not null)
        {
            (option.VerticalAlignment, option.Margin) = (VerticalAlignment.Center, new(Tokens.Space.Card, 0, 0, 0));
            DockPanel.SetDock(option, Dock.Left);
            bar.Children.Add(option);
        }
        bar.Children.Add(buttons);
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        DockPanel.SetDock(_note, Dock.Bottom);
        _note.Margin = new(Tokens.Space.Card, Tokens.Space.Gap, Tokens.Space.Card, 0);
        root.Children.Add(bar);
        root.Children.Add(_note);
        root.Children.Add(_scroll);
        Content = root;
        Themes.Apply(this, theme, root);
        _scroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) Fit(); };   // the viewport is known only after layout
        _scroll.PreviewMouseWheel += (_, e) => { e.Handled = true; Zoom(e.Delta > 0 ? ZoomStep : 1 / ZoomStep); };
        PreviewKeyDown += Key;
        Loaded += (_, _) => _screen.Focus();   // the arrow keys go to the screen's own handler
        Fit();
    }

    /// <summary>--reading-sample: 예시 보기 on, as a click would.</summary>
    internal void DemoSample()
    {
        _sample.IsChecked = true;
        SampleToggled?.Invoke(true);
    }

    /// <summary>The page's placing line and what 되돌리기 / 처음으로 / 적용 can do now.</summary>
    public void SetState(string note, bool canBack, bool changed)
    {
        _note.Text = note;
        Fit();   // called after each redraw: the screen may have its frame size only now
        (_back.IsEnabled, _reset.IsEnabled) = (canBack, changed);
        _apply.IsEnabled = changed;   // nothing to apply before the session starts or after a step back to the start
        UiBuild.MarkUnapplied(_apply, changed);
    }

    void Key(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case System.Windows.Input.Key.Escape: Close(); break;
            case System.Windows.Input.Key.Enter when e.OriginalSource is not Button && _apply.IsVisible && _apply.IsEnabled: ApplyRequested?.Invoke(); break;   // Enter on a button presses that button
            case System.Windows.Input.Key.Z when ctrl: BackRequested?.Invoke(); break;
            case System.Windows.Input.Key.OemPlus or System.Windows.Input.Key.Add: Zoom(ZoomStep); break;
            case System.Windows.Input.Key.OemMinus or System.Windows.Input.Key.Subtract: Zoom(1 / ZoomStep); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Zooms about the view's centre. Not mid-drag: the drag measures from its press at the old scale.</summary>
    void Zoom(double by)
    {
        if (_screen.IsMouseCaptured) return;
        double before = _view.Width;
        _zoom = Math.Clamp(_zoom * by, MinZoom, MaxZoom);
        Fit();
        if (double.IsNaN(before) || before <= 0 || double.IsNaN(_view.Width)) return;
        double ratio = _view.Width / before, cx = _scroll.HorizontalOffset + _scroll.ViewportWidth / 2, cy = _scroll.VerticalOffset + _scroll.ViewportHeight / 2;
        _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(cx * ratio - _scroll.ViewportWidth / 2);
        _scroll.ScrollToVerticalOffset(cy * ratio - _scroll.ViewportHeight / 2);
    }

    /// <summary>The view at the zoom: 1× fits the frame in the window, more scrolls.</summary>
    void Fit()
    {
        double w = _screen.Width, h = _screen.Height, room = _scroll.ViewportWidth, tall = _scroll.ViewportHeight;
        if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0 || room <= 0 || tall <= 0) return;   // before layout: ScrollChanged fits it
        double k = Math.Min(room / w, tall / h) * _zoom;
        (_view.Width, _view.Height) = (w * k, h * k);
        _zoomText.Text = $"{_zoom:0.#}×";
    }
}
