using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Net;

namespace GaugeOverlay.App.UI;

/// <summary>참가 요청 승인: "민수 (v1.0.9)님이 파티에 참가하려고 합니다" with 허용 / 거절, at the bottom right of the
/// screen, above the game. It never takes the keyboard from the game (no activation), so it can be answered with the
/// mouse or ignored. Several stack upwards; each closes when answered or when the joiner gives up.</summary>
sealed class JoinRequestPopup : Window
{
    const double PopupWidth = 320, PopupHeight = 104, Gap = 8;
    static readonly List<JoinRequestPopup> Open = [];

    readonly ulong _id;

    JoinRequestPopup(JoinRequest request, Action<bool> answer)
    {
        _id = request.Id;
        (WindowStyle, ResizeMode, Topmost, ShowInTaskbar, ShowActivated, Width, Height) =
            (WindowStyle.None, ResizeMode.NoResize, true, false, false, PopupWidth, PopupHeight);
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x27, 0x34));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x5A, 0x6C));
        BorderThickness = new Thickness(1);
        Title = "GaugeOverlay 참가 요청";

        var text = new TextBlock
        {
            // The name is the joiner's to choose; the device code tells a copied name apart (신뢰한 기기 shows the same code).
            Text = $"{request.Name} (v{request.App}, 기기 {request.Device[..8]})님이 파티에 참가하려고 합니다.",
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        };
        Button ButtonFor(string label, bool ok)
        {
            var b = new Button { Content = label, Width = 80, Margin = new Thickness(6, 0, 0, 0) };
            b.Click += (_, _) => { Close(); answer(ok); };
            return b;
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ButtonFor("허용", true));
        buttons.Children.Add(ButtonFor("거절", false));
        var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        body.Children.Add(text);
        body.Children.Add(buttons);
        Content = body;
        Closed += (_, _) => { Open.Remove(this); Restack(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.Set(new WindowInteropHelper(this).Handle, ExStyle.ToolWindow | ExStyle.NoActivate, true);
    }

    /// <summary>Shows a request; <paramref name="answer"/> gets true for 허용, false for 거절.</summary>
    public static void Ask(JoinRequest request, Action<bool> answer)
    {
        var popup = new JoinRequestPopup(request, answer);
        Open.Add(popup);
        Restack();
        popup.Show();
    }

    /// <summary>The joiner gave up or left: its request closes unanswered.</summary>
    public static void Withdraw(ulong id)
    {
        foreach (var popup in Open.Where(p => p._id == id).ToList()) popup.Close();
    }

    /// <summary>The session ended: every request closes.</summary>
    public static void CloseAll()
    {
        foreach (var popup in Open.ToList()) popup.Close();
    }

    /// <summary>Bottom right of the work area, the oldest lowest.</summary>
    static void Restack()
    {
        var area = SystemParameters.WorkArea;
        for (int i = 0; i < Open.Count; i++)
            (Open[i].Left, Open[i].Top) = (area.Right - PopupWidth - Gap, area.Bottom - (PopupHeight + Gap) * (i + 1));
    }
}
