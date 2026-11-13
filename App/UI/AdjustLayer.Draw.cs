using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Geometry = GaugeOverlay.Core.Vision.Geometry;

namespace GaugeOverlay.App.UI;

/// <summary>The layer's drawing: each part's box in its colour (green while it reads well), the bar's slots, the rows split into
/// their boxes and icons over timers, the boss row's slash, size handles; and the toolbar with the picked part's values.</summary>
public sealed partial class AdjustLayer
{
    static readonly (AdjustPart Part, string Name, Color Color)[] Parts =
    [
        (AdjustPart.스킬창, "스킬창", Color.FromRgb(0xFF, 0xD2, 0x4A)), (AdjustPart.버프, "버프 줄", Color.FromRgb(0x1B, 0xA8, 0xE0)),
        (AdjustPart.디버프, "디버프 줄", Color.FromRgb(0xE8, 0x26, 0x2B)), (AdjustPart.보스, "보스 줄", Color.FromRgb(0xA0, 0x4F, 0xB0)),
        (AdjustPart.전분, "전분", Color.FromRgb(0xF0, 0x8A, 0x3C)),
    ];
    static readonly Color Good = Color.FromRgb(0x3B, 0xD1, 0x6F);
    const string Keys = "끌기: 옮기기 · 모서리 네모: 크기 · ←↑→↓ 1px (Shift 10px) · Alt+↑↓ 또는 + − 크기 (Shift 10) · Tab 다음 상자 · Ctrl+Z 처음으로 · Enter 적용 · Esc 닫기";

    readonly TextBlock _part = new() { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    readonly TextBlock _focus = new() { Text = "키보드를 쓰려면 이 화면을 한 번 누르세요.", Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A)), FontSize = 11 };
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Opacity = 0.85, MaxWidth = 560 };
    readonly TranslateTransform _toolbarAt = new();
    Point? _toolbarDrag;
    Button? _apply;   // lit while the edit differs from the saved fit (FitAdjust.Changed)

    /// <summary>The toolbar at the top centre of the area; dragged by its background out of the way.</summary>
    FrameworkElement BuildToolbar()
    {
        Button B(string text, Action click)
        {
            var b = new Button { Content = text, Padding = new(12, 3, 12, 3), Margin = new(0, 0, 6, 0), Focusable = false };
            b.Click += (_, _) => click();
            return b;
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 6, 0, 0) };
        buttons.Children.Add(B("되돌리기", () => { if (_adj is { } a) Changed(a.Reverted()); }));
        _apply = B("적용", Apply);
        buttons.Children.Add(_apply);
        buttons.Children.Add(B("닫기", Dismiss));
        var panel = new StackPanel();
        panel.Children.Add(_part);
        panel.Children.Add(_focus);
        panel.Children.Add(new TextBlock { Text = Keys, Foreground = Brushes.White, Opacity = 0.75, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 });
        panel.Children.Add(buttons);
        panel.Children.Add(_result);
        var bar = new Border
        {
            Child = panel, Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x22)), CornerRadius = new(6),
            Padding = new(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new(0, 24, 0, 0), RenderTransform = _toolbarAt, Cursor = Cursors.SizeAll,
        };
        bar.MouseLeftButtonDown += (_, e) => { _toolbarDrag = e.GetPosition(this); bar.CaptureMouse(); e.Handled = true; };
        bar.MouseMove += (_, e) =>
        {
            if (_toolbarDrag is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
            var at = e.GetPosition(this);
            (_toolbarAt.X, _toolbarAt.Y) = (_toolbarAt.X + at.X - from.X, _toolbarAt.Y + at.Y - from.Y);
            _toolbarDrag = at;
        };
        bar.MouseLeftButtonUp += (_, _) => bar.ReleaseMouseCapture();
        bar.LostMouseCapture += (_, _) => _toolbarDrag = null;
        return bar;
    }

    void Redraw()
    {
        if (_apply is { } apply) UiBuild.MarkUnapplied(apply, _adj?.Changed == true);
        _boxes.Children.Clear();
        if (_adj is not { } a) return;
        foreach (var (part, name, color) in Parts)
        {
            bool picked = part == _selected;
            var brush = new SolidColorBrush(ReadsWell(part) ? Good : color);
            var thin = new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B));
            var r = a.RectOf(part);
            Box(r, brush, picked ? 3 : 1.5, name);
            foreach (var (x1, y1, x2, y2) in a.Guides(part)) Line(x1, y1, x2, y2, thin);
            Handle(a.Handle(part), part, brush, picked);
        }
        _part.Text = Describe(a, _selected);
    }

    /// <summary>After 적용 the game is brought to the front for the ready slots, so the keys go to it until the layer is clicked.</summary>
    void ShowFocus() => _focus.Visibility = IsActive ? Visibility.Collapsed : Visibility.Visible;

    bool ReadsWell(AdjustPart part) => _reads?.Of(part) == true;

    /// <summary>The toolbar's first line: the picked part and its values.</summary>
    static string Describe(FitAdjust a, AdjustPart part)
    {
        var (x, y) = a.Offset(part);
        string Px(double v) => (int)Math.Round(v) is var n && n < 0 ? $"−{-n}" : $"+{n}";
        return part switch
        {
            AdjustPart.스킬창 => $"스킬창 · HUD {TestFits.Offsets(a.Current, Geometry.Scale(a.Vp, a.Current))}",
            AdjustPart.버프 => $"버프 줄 · 버프 크기 {a.BuffSize}% (디버프 줄과 같이) · {Px(x)}, {Px(y)} px",
            AdjustPart.디버프 => $"디버프 줄 · 버프 크기 {a.BuffSize}% (버프 줄과 같이) · {Px(x)}, {Px(y)} px",
            AdjustPart.전분 => $"전분 · {a.Panel.Width}×{a.Panel.Height} · {Px(x)}, {Px(y)} px",
            _ => $"보스 줄 · {a.BossPercent}% · {Px(x)}, {Px(y)} px",
        };
    }

    double Lx(double screenX) => (screenX - _adj!.Vp.X) / Dpi;
    double Ly(double screenY) => (screenY - _adj!.Vp.Y) / Dpi;

    /// <summary>A box's outline drawn just outside <paramref name="r"/> (WPF strokes inside a Rectangle), so it never covers
    /// the pixels read: a buff's icon edge or the timer under it.</summary>
    void Box(PixelRect r, Brush brush, double thickness, string? name)
    {
        var box = new Rectangle { Width = r.Width / Dpi + 2 * thickness, Height = r.Height / Dpi + 2 * thickness, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false };
        Canvas.SetLeft(box, Lx(r.X) - thickness);
        Canvas.SetTop(box, Ly(r.Y) - thickness);
        _boxes.Children.Add(box);
        if (name is null) return;
        var label = new TextBlock { Text = name, Foreground = brush, FontSize = 12, Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)), IsHitTestVisible = false };
        Canvas.SetLeft(label, Lx(r.X));
        Canvas.SetTop(label, Math.Max(0, Ly(r.Y) - 17));
        _boxes.Children.Add(label);
    }

    void Line(double x1, double y1, double x2, double y2, Brush brush) => _boxes.Children.Add(new Line
    {
        X1 = Lx(x1), Y1 = Ly(y1), X2 = Lx(x2), Y2 = Ly(y2), Stroke = brush, StrokeThickness = 1, IsHitTestVisible = false,
    });

    /// <summary>A size handle: a square inside the box's corner (the bar's and buff row's on the left, the others' on the right).</summary>
    void Handle((int X, int Y) at, AdjustPart part, Brush brush, bool picked)
    {
        double h = (picked ? HandlePx : HandlePx * 0.7) / Dpi;
        var square = new Rectangle { Width = h, Height = h, Fill = brush, IsHitTestVisible = false };
        Canvas.SetLeft(square, part is AdjustPart.스킬창 or AdjustPart.버프 ? Lx(at.X) : Lx(at.X) - h);
        Canvas.SetTop(square, Ly(at.Y) - h);
        _boxes.Children.Add(square);
    }
}
