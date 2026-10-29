using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The top bar, always shown: the wordmark, 프로필 1–5 (the overlay's spots and sizes, as the tray and hotkeys
/// switch them) and 편집 중 (<see cref="_context"/>: which 직업각인 the class settings edit).</summary>
public partial class SettingsWindow
{
    RadioButton[] _profileButtons = [];

    /// <summary>프로필 n was picked in the top bar: App switches, as from the tray, and reloads 설정.</summary>
    public event Action<int>? ProfileRequested;

    void BuildTopBar()
    {
        TopBar.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        TopBar.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        var mark = new TextBlock
        {
            Text = "GaugeOverlay", FontFamily = Themes.Cinzel, FontSize = 20, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 32, 0),
        };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        TopBarContent.Children.Add(mark);

        var profiles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 32, 0) };
        profiles.Children.Add(new TextBlock { Text = "프로필", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0), Opacity = 0.8 });
        _profileButtons = new RadioButton[OverlayProfile.Count];
        for (int i = 0; i < OverlayProfile.Count; i++)
        {
            int n = i;
            var b = Toggle($"{i + 1}", "profile", i == _s.ActiveProfile);
            (b.Margin, b.MinWidth, b.Padding) = (new(0, 0, 2, 0), 36, new(10, 4, 10, 4));
            b.ToolTip = $"오버레이 프로필 {i + 1}: 창의 위치와 크기를 프로필마다 따로 저장합니다.";
            b.Checked += (_, _) => { if (n != _s.ActiveProfile) { RecordStep(); ProfileRequested?.Invoke(n); ShowProfileButtons(); } };
            profiles.Children.Add(_profileButtons[i] = b);
        }
        TopBarContent.Children.Add(profiles);

        _context.VerticalAlignment = VerticalAlignment.Center;
        TopBarContent.Children.Add(_context);
    }

    /// <summary>The profile buttons as the settings say (switched from the tray or a hotkey, or refused mid-drag).</summary>
    void ShowProfileButtons()
    {
        for (int i = 0; i < _profileButtons.Length; i++) _profileButtons[i].IsChecked = i == _s.ActiveProfile;
    }
}
