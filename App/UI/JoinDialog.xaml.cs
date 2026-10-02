using System.Windows;

namespace GaugeOverlay.App.UI;

/// <summary>Asks for a party token. <see cref="Ask"/> returns it, or null when cancelled.</summary>
public partial class JoinDialog : Window
{
    JoinDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TokenBox.Focus();
    }

    void OnJoin(object sender, RoutedEventArgs e) => DialogResult = TokenBox.Text.Trim().Length > 0;

    public static string? Ask()
    {
        var d = new JoinDialog();
        return d.ShowDialog() == true ? d.TokenBox.Text.Trim() : null;
    }
}
