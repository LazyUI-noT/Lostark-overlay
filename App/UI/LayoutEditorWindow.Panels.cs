using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>레이아웃 편집기's panels, over the screenshot: top left the class (기본 first, then every 클래스 · 직업각인);
/// under it 칸 옵션, the options of what was clicked (a box, a buff bar, the plate, or the gauge: 아덴 게이지 and that
/// class's 막대); top right 편집, with ▶ 실행해 보기. Each folds to its title bar and drags by it.</summary>
public sealed partial class LayoutEditorWindow
{
    const double ClassWidth = 300, BoxWidth = 460;
    const double TrayRoom = 56;   // under the left panels: the 패널 tray
    const double KeepShown = 40;   // a dragged panel keeps this much of its title bar in the window

    readonly List<TranslateTransform> _cards = [];

    /// <summary>The screenshot across the window, the panels over it: [class, box options under it] on the left, the
    /// fixed panel top right, 전체 화면 bottom right, and bottom left the 패널 tray that hides and shows them.</summary>
    Grid Layout(UIElement view, UIElement panel, UIElement fullScreen)
    {
        var left = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, TrayRoom) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition());   // the box options scroll in what is left
        var box = BoxPanel();
        var cls = ClassPanel();
        Grid.SetRow(box, 1);
        left.Children.Add(cls);
        left.Children.Add(box);
        var tray = PanelTray([("편집", panel), ("클래스", cls), ("칸 옵션", box), ("전체 화면", fullScreen)]);
        return new Grid { Children = { view, left, panel, fullScreen, tray } };
    }

    /// <summary>패널 (bottom left, never hidden itself): a tick per panel to hide it and see more of the screenshot;
    /// what is hidden is saved at once (Settings.EditorHidden), 적용 or not.</summary>
    UIElement PanelTray(IReadOnlyList<(string Name, UIElement Panel)> panels)
    {
        var list = new StackPanel { Visibility = Visibility.Collapsed, Margin = new(0, 0, 0, 6) };
        foreach (var (name, panel) in panels)
        {
            bool shown = !_live.EditorHidden.Contains(name);
            panel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            var c = new CheckBox { Content = name, IsChecked = shown, MinWidth = 0 };
            c.Click += (_, _) =>
            {
                bool on = c.IsChecked == true;
                panel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                foreach (var s in new[] { _live, _s })
                    if (on) s.EditorHidden.Remove(name); else s.EditorHidden.Add(name);
                PrefsChanged?.Invoke();
            };
            list.Children.Add(c);
        }
        var open = new ToggleButton { Content = "패널 ▴", Padding = new(10, 3, 10, 3), ToolTip = "편집기 위의 패널을 숨기거나 다시 보입니다." };
        open.Checked += (_, _) => { list.Visibility = Visibility.Visible; open.Content = "패널 ▾"; };
        open.Unchecked += (_, _) => { list.Visibility = Visibility.Collapsed; open.Content = "패널 ▴"; };
        var tray = new Border
        {
            Child = new StackPanel { Children = { list, open } }, Padding = new(8), Margin = new(12), CornerRadius = new(6),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
        };
        Panelled(tray);
        return tray;
    }

    /// <summary>전체 화면 on or off: the window changes size, so every panel goes back where it starts.</summary>
    void FullScreenLayout() => _cards.ForEach(at => (at.X, at.Y) = (0, 0));

    /// <summary>The class picked: what the sample shows and what changes go to.</summary>
    UIElement ClassPanel()
    {
        var pick = new ComboBox { Width = ClassWidth, HorizontalAlignment = HorizontalAlignment.Left };
        var items = new List<ComboBoxItem> { new() { Content = "기본", Tag = null } };
        foreach (var (family, classes) in _chars.ClassTree)
            foreach (var (cls, subs) in classes)
                foreach (var sub in subs)
                    items.Add(new ComboBoxItem { Content = $"{cls} · {sub}", Tag = (family, cls, sub) });
        pick.ItemsSource = items;
        pick.SelectedItem = items.FirstOrDefault(i => i.Tag is (string, string c, string s) && _s.ActiveCharacter == $"{c} · {s}") ?? items[0];
        pick.SelectionChanged += (_, _) =>
        {
            if (pick.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is (string f, string c, string s)) _s.PickCharacter(f, c, s);
            else _s.ActiveCharacter = null;
            ShowSample();
            _forms.Reload();
            Pick(null, null);
            Relayout();
        };
        return Card("클래스", pick, HorizontalAlignment.Left);
    }

    /// <summary>칸 옵션: the options of what was clicked; the gauge's (<see cref="SettingsWindow.GaugeBox"/>, shown while it
    /// is the part picked) here too, rather than under 클래스, where they ran under the 패널 tray.</summary>
    UIElement BoxPanel()
    {
        _forms.ShowBox(null, plate: false);
        _forms.GaugeBox.Margin = new(0, 0, 0, 12);
        var options = new StackPanel { Children = { _forms.BoxTab, _forms.GaugeBox } };
        return Card("칸 옵션", new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = BoxWidth }, HorizontalAlignment.Left);
    }

    /// <summary>A panel's opaque background and outline, in 설정's theme.</summary>
    static void Panelled(Border b)
    {
        b.SetResourceReference(Border.BackgroundProperty, PanelBrushKey);
        b.SetResourceReference(Border.BorderBrushProperty, Themes.StrokeBrushKey);
        b.BorderThickness = new(1);
    }

    /// <summary>A panel over the screenshot, in the window's colours, as tall as its content, under a title bar: ▾ folds it
    /// to the bar (▸ opens it again), and dragging the bar moves the panel, never quite out of the window.</summary>
    Border Card(string title, UIElement body, HorizontalAlignment side)
    {
        var fold = new ToggleButton { Content = "▾", MinWidth = 0, MinHeight = 0, Padding = new(6, 0, 6, 0), ToolTip = "접기 / 펴기" };
        fold.Checked += (_, _) => { body.Visibility = Visibility.Collapsed; fold.Content = "▸"; };
        fold.Unchecked += (_, _) => { body.Visibility = Visibility.Visible; fold.Content = "▾"; };
        var head = new DockPanel { Background = Brushes.Transparent, Margin = new(0, 0, 0, 6), Cursor = Cursors.SizeAll };   // hit-testable between its items
        DockPanel.SetDock(fold, Dock.Right);
        head.Children.Add(fold);
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) });
        DockPanel.SetDock(head, Dock.Top);

        var at = new TranslateTransform();
        var card = new Border
        {
            Child = new DockPanel { Children = { head, body } }, Padding = new(12), Margin = new(12), CornerRadius = new(6),
            HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Top, RenderTransform = at,
        };
        Panelled(card);

        Point from = default, home = default;   // home: where the panel stands undragged
        (double X, double Y) start = default;
        head.MouseLeftButtonDown += (_, e) =>
        {
            (from, start) = (e.GetPosition(this), (at.X, at.Y));
            var now = card.TranslatePoint(default, this);
            home = new(now.X - at.X, now.Y - at.Y);
            head.CaptureMouse();
            e.Handled = true;
        };
        head.MouseMove += (_, e) =>
        {
            if (!head.IsMouseCaptured) return;
            var now = e.GetPosition(this);
            at.X = Math.Max(-home.X - card.ActualWidth + KeepShown, Math.Min(ActualWidth - home.X - KeepShown, start.X + now.X - from.X));
            at.Y = Math.Max(-home.Y, Math.Min(ActualHeight - home.Y - KeepShown, start.Y + now.Y - from.Y));
        };
        head.MouseLeftButtonUp += (_, _) => head.ReleaseMouseCapture();
        _cards.Add(at);
        return card;
    }

    /// <summary>▶ 실행해 보기 and 초기화: while it runs, a click uses the box (a cooling one: 1 s left) instead of moving it.</summary>
    UIElement RunButtons()
    {
        var run = new ToggleButton { Content = "▶ 실행해 보기", Padding = new(10, 3, 10, 3), Margin = new(0, 6, 8, 0) };
        var reset = new Button { Content = "초기화", Padding = new(10, 3, 10, 3), Margin = new(0, 6, 0, 0), IsEnabled = false };
        run.Checked += (_, _) => { _play.Start(); reset.IsEnabled = true; run.Content = "■ 멈추기"; };
        run.Unchecked += (_, _) => { _play.Stop(); reset.IsEnabled = false; run.Content = "▶ 실행해 보기"; };
        reset.Click += (_, _) => _play.Reset();
        return Line(run, reset);
    }

    /// <summary>Picks the box (null: the plate) of your overlay or buff window for the side panels; null renderer: none.</summary>
    void Pick(OverlayRenderer? r, Part? part)
    {
        (_picked, _pickedDebuff) = (r is null ? null : (r, part), null);
        _forms.ShowBox(part, plate: r is not null && part is null);
        ShowPick();
    }

    /// <summary>Picks a debuff box (null: none) for 칸 옵션, its 너비 and 높이.</summary>
    void PickDebuff(int? i)
    {
        if (i is not { } d) { Pick(null, null); return; }
        (_picked, _pickedDebuff) = (null, d);
        _forms.ShowDebuffBox(d);
        ShowPick();
    }

    /// <summary>Outlines the picked box on the screenshot; with 칸 옮기기 on, its corner grip shows as a white square.</summary>
    void ShowPick()
    {
        _grip.Visibility = Visibility.Collapsed;
        (OverlayRenderer? On, Rect? Rect) shown = _pickedDebuff is { } d ? (_debuffs, _debuffs.DebuffRect(d))
            : _picked is ({ } r, { } part) ? (r, r.PartRect(part)) : (null, null);
        var (on, rect) = shown;
        bool visible = rect is not null && on?.Visibility == Visibility.Visible;   // not on a window turned off (디버프 창)
        _pick.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible || rect is not { } b || on is null) return;
        (_pick.Width, _pick.Height) = (b.Width + 4, b.Height + 4);
        Canvas.SetLeft(_pick, Canvas.GetLeft(on) + b.X - 2);
        Canvas.SetTop(_pick, Canvas.GetTop(on) + b.Y - 2);
        if (!_moveBoxes || on == _buffs || on.ShowsReceivedLayout) return;
        double g = GripSize(b);
        (_grip.Width, _grip.Height, _grip.StrokeThickness) = (g, g, 1 / (_v?.Scale ?? 1));
        Canvas.SetLeft(_grip, Canvas.GetLeft(on) + b.Right - g);
        Canvas.SetTop(_grip, Canvas.GetTop(on) + b.Bottom - g);
        _grip.Visibility = Visibility.Visible;
    }
}
