using System.Windows;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Asks for a party token. <see cref="Ask"/> returns it, or null when cancelled.</summary>
public partial class JoinDialog : Window
{
    JoinDialog(UiTheme theme)
    {
        InitializeComponent();
        Themes.Apply(this, theme, Root);
        Themes.Primary(JoinButton);
        // 참가 waits for a token: pressed on an empty box it used to close the dialog as if cancelled.
        TokenBox.TextChanged += (_, _) => JoinButton.IsEnabled = TokenBox.Text.Trim().Length > 0;
        Loaded += (_, _) => TokenBox.Focus();
    }

    void OnJoin(object sender, RoutedEventArgs e) => DialogResult = true;

    public static string? Ask(UiTheme theme)
    {
        var d = new JoinDialog(theme);
        return d.ShowDialog() == true ? d.TokenBox.Text.Trim() : null;
    }
}
