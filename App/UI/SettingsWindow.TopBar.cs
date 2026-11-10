using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        profiles.Children.Add(new TextBlock { Text = "배치", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0), Opacity = 0.8 });
        _profileButtons = new RadioButton[OverlayProfile.Count];
        for (int i = 0; i < OverlayProfile.Count; i++)
        {
            int n = i;
            var b = Toggle($"{i + 1}", "profile", i == _s.ActiveProfile);
            (b.Margin, b.MinWidth, b.Padding) = (new(0, 0, 2, 0), 36, new(10, 4, 10, 4));
            b.ToolTip = $"오버레이 배치 {i + 1}: 창의 위치와 크기를 배치마다 따로 저장합니다.";
            b.Checked += (_, _) => { if (n != _s.ActiveProfile) { RecordStep(); ProfileRequested?.Invoke(n); ShowProfileButtons(); } };
            profiles.Children.Add(_profileButtons[i] = b);
        }
        _testProfile.GroupName = "profile";
        (_testProfile.Margin, _testProfile.Padding, _testProfile.IsHitTestVisible, _testProfile.Focusable, _testProfile.Style) =
            (new(6, 0, 2, 0), new(10, 4, 10, 4), false, false, TryFindResource(typeof(ToggleButton)) as Style);
        _testProfile.ToolTip = "테스트 모드를 켜면 자동으로 이 배치가 됩니다. 옵션과 위치가 따로 저장되고, 끄면 전에 쓰던 배치로 돌아갑니다.";
        profiles.Children.Add(_testProfile);
        TopBarContent.Children.Add(profiles);

        _context.VerticalAlignment = VerticalAlignment.Center;
        TopBarContent.Children.Add(_context);
        TopBar.SizeChanged += (_, _) => FitCopyButton();
    }

    const string CopyLabel = "다른 직업각인에 복사…";
    const double CopyLabelFits = 1120;   // the top bar's width that holds the long label (it clipped at the 1000 px minimum)
    Button? _copyButton;

    /// <summary>A narrow 설정: the copy button says 복사… (the full words stay in its tooltip and name).</summary>
    void FitCopyButton()
    {
        if (_copyButton is { } b) b.Content = TopBar.ActualWidth >= CopyLabelFits || TopBar.ActualWidth == 0 ? CopyLabel : "복사…";
    }

    /// <summary>The profile buttons as the settings say (switched from the tray or a hotkey, or refused mid-drag).</summary>
    void ShowProfileButtons()
    {
        for (int i = 0; i < _profileButtons.Length; i++)
            (_profileButtons[i].IsChecked, _profileButtons[i].IsEnabled) = (!_inTestProfile && i == _s.ActiveProfile, !_inTestProfile);
        _testProfile.IsChecked = _inTestProfile;
    }

    readonly RadioButton _testProfile = new() { Content = "Test" };
    bool _inTestProfile;

    /// <summary>The live settings are now [Test]'s or the ones before it: everything is shown afresh, and undo can't reach across.</summary>
    public void SettingsSwapped(bool test)
    {
        _inTestProfile = test;
        _history.Clear();
        _lastChange = default;
        BuildAll();
        ShowProfileButtons();
        ResetBaseline();
        SettingsChanged?.Invoke();
        LayoutChanged?.Invoke();
    }
}
