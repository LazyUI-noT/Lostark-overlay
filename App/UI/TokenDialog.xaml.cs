using System.Windows;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Shows the just-generated host token as selectable text, so it can be copied by hand if the
/// clipboard auto-copy silently failed (clipboard locked by another app) or was missed.</summary>
public partial class TokenDialog : Window
{
    TokenDialog(string token, int port, UiTheme theme)
    {
        InitializeComponent();
        Themes.Apply(this, theme, Root);
        Themes.Primary(CopyButton);
        TokenBox.Text = token;
        _port = port;
        ShowCopied(TryCopy());   // before showing: SizeToContent measures the window with its hint
        Loaded += (_, _) => { TokenBox.Focus(); TokenBox.SelectAll(); };
        _copiedReset.Tick += (_, _) => { _copiedReset.Stop(); CopyButton.Content = "복사"; };
    }

    readonly int _port;
    readonly System.Windows.Threading.DispatcherTimer _copiedReset = new() { Interval = TimeSpan.FromSeconds(2) };

    void OnCopy(object sender, RoutedEventArgs e) => ShowCopied(TryCopy());

    bool TryCopy()
    {
        try { Clipboard.SetText(TokenBox.Text); return true; }
        catch (System.Runtime.InteropServices.COMException) { return false; }   // clipboard held by another app
    }

    /// <summary>The hint says whether the token is on the clipboard: the auto-copy can fail, and then it must be copied by hand.</summary>
    void ShowCopied(bool copied)
    {
        Hint.Text = (copied ? "파티원에게 이 토큰을 보내 주세요. 클립보드에 복사했습니다."
            : "파티원에게 이 토큰을 보내 주세요. 클립보드를 쓸 수 없어 복사하지 못했습니다. [복사]를 다시 누르거나 직접 선택해 복사하세요.")
            + $" 공유기에서 UDP {_port} 포트를 열어 두어야 합니다.";
        CopyButton.Content = copied ? "복사됨" : "복사";
        if (copied) { _copiedReset.Stop(); _copiedReset.Start(); }   // back to 복사, so a second press shows again
    }

    public static void ShowToken(string token, int port, UiTheme theme) => new TokenDialog(token, port, theme).ShowDialog();
}
