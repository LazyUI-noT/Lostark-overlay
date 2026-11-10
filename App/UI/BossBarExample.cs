using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 레이드's examples (user, 2026-11-09): a boss bar cut from footage (App/Assets/examples) with the label the
/// switch adds drawn live, as the app draws it over the game (<see cref="OutlinedText"/>, the texts from Core's own views). Shown
/// whether the switch is on or not: it says what turning it on adds.</summary>
static class BossBarExample
{
    const double MaxWidth = 520;   // the 760 px crops at about 0.68: the game's name text stays readable
    const double Indent = 28;   // under the check box's text, not its box
    static readonly SolidColorBrush White = Frozen(Colors.White), Cyan = Frozen(OutlinedText.InBarInk);

    /// <summary>구원의 종탑 2관문's bar (Bossbar_bug 15.11.23): its HP, easy to read, after the name. The place is the crop's
    /// (the clip is letterboxed 21:9, so Geometry.RefBossHpLabel doesn't map onto it).</summary>
    public static FrameworkElement? EasyHp() => Make("boss-hp.png", 760, 84,   // 84: ends under the purple bar, before the party's text
        "보스 체력 간소화 예시: 보스 이름 옆에 남은 체력이 조와 억으로 보입니다.",
        (new BossHpView(1_080_282_443_158, 0, default).Text, new Rect(474, 0, 282, 30), White, false, 0.55));   // the name ends at x≈465

    /// <summary>세르카's 대난투 (serka-shadow 0:26, 1920×1080 from the top): the crop starts at (606, 44), so Geometry's 1080p
    /// places (RefShadowBar, RefShadowDealt) move by that much.</summary>
    public static FrameworkElement? Daenantu()
    {
        const long LastHp = 1_080_282_443_158, Dealt = 82_774_990_867;
        var view = new ShadowView(LastHp - Dealt, 166, default, Dealt, default);
        return Make("daenantu.png", 760, 92,
            "대난투 체력 표시 예시: 채우는 막대 자리에 남은 줄과 체력이, 대난투 숫자 옆에 넣은 피해가 보입니다.",
            (view.Text, new Rect(638 - 606, 101 - 44, 645, 21), Cyan, true, 0.85),
            (EasyRead.Of(Dealt), new Rect(1115 - 606, 56 - 44, 245, 30), White, false, 0.6));
    }

    static FrameworkElement? Make(string file, double width, double height, string name,
        params (string Text, Rect At, Brush Ink, bool Centred, double Share)[] labels)
    {
        if (Load(file) is not { } picture) return null;   // a missing resource: no example, the row still works
        var canvas = new Canvas { Width = width, Height = height };
        var image = new System.Windows.Controls.Image { Source = picture, Width = width, Height = height, Stretch = Stretch.Fill };
        AutomationProperties.SetName(image, name);   // on the Image: a Border has no automation peer
        canvas.Children.Add(image);
        foreach (var (text, at, ink, centred, share) in labels)
        {
            var line = new OutlinedText(ink, centred, share) { Text = text, Width = at.Width, Height = at.Height };
            Canvas.SetLeft(line, at.X);
            Canvas.SetTop(line, at.Y);
            canvas.Children.Add(line);
        }
        var frame = new Border
        {
            Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform },
            MaxWidth = MaxWidth, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(Indent, 0, 0, 10),
            CornerRadius = new(4), BorderThickness = new(1), ClipToBounds = true,
        };
        frame.SetResourceReference(Border.BorderBrushProperty, Themes.StrokeBrushKey);
        return frame;
    }

    static BitmapImage? Load(string file)
    {
        try
        {
            var b = new BitmapImage(new Uri($"pack://application:,,,/Assets/examples/{file}"));
            b.Freeze();
            return b;
        }
        catch (Exception e) when (e is IOException or FileFormatException or NotSupportedException)
        {
            System.Diagnostics.Trace.WriteLine($"[ui] example {file} unreadable: {e.Message}");
            return null;
        }
    }

    static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
