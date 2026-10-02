using System.Windows;

namespace GaugeOverlay.App.UI;

/// <summary>Shows the just-generated host token as selectable text, so it can be copied by hand if the
/// clipboard auto-copy silently failed (clipboard locked by another app) or was missed.</summary>
public partial class TokenDialog : Window
{
    TokenDialog(string token, int port)
    {
        InitializeComponent();
        TokenBox.Text = token;
        Hint.Text = $"파티원에게 이 토큰을 보내 주세요. 클립보드에 자동으로 복사했습니다. 공유기에서 UDP {port} 포트를 열어 두어야 합니다.";
        Loaded += (_, _) => { TokenBox.Focus(); TokenBox.SelectAll(); TryCopy(); };
    }

    void OnCopy(object sender, RoutedEventArgs e) => TryCopy();

    void TryCopy()
    {
        try { Clipboard.SetText(TokenBox.Text); } catch (System.Runtime.InteropServices.COMException) { }
    }

    public static void ShowToken(string token, int port) => new TokenDialog(token, port).ShowDialog();
}
