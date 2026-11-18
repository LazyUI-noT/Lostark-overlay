using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The top bar, always shown: the wordmark, 프로필 1–5 (the overlay's spots and sizes, as the tray and hotkeys
/// switch them) and 편집 중 (<see cref="_context"/>: which 직업각인 the class settings edit).</summary>
public partial class SettingsWindow
{
    /// <summary>The top bar's profile buttons only (not a window resource: that would replace the theme's): outline (clear fill,
    /// muted 1px line) unless picked (accent fill), with hover and keyboard focus kept visible.</summary>
    static readonly Style ProfileButtonStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
<Style TargetType="RadioButton" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <Setter Property="Foreground" Value="{DynamicResource TextFillColorPrimaryBrush}" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderBrush" Value="{DynamicResource ControlStrongStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="RadioButton">
                        <Border x:Name="Box" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Box" Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource AccentFillColorDefaultBrush}" />
                                <Setter TargetName="Box" Property="BorderThickness" Value="2" />
                            </Trigger>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="Box" Property="Background" Value="{DynamicResource AccentFillColorDefaultBrush}" />
                                <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource AccentFillColorDefaultBrush}" />
                                <Setter Property="Foreground" Value="{DynamicResource TextOnAccentFillColorPrimaryBrush}" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.5" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
""");

    RadioButton[] _profileButtons = [];

    /// <summary>프로필 n was picked in the top bar: App switches, as from the tray, and reloads 설정.</summary>
    public event Action<int>? ProfileRequested;

    void BuildTopBar()
    {
        TopBar.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        TopBar.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        var mark = new TextBlock
        {
            Text = "GaugeOverlay", FontFamily = Themes.Cinzel, FontSize = Tokens.Font.Title, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(Tokens.Space.Tight, 0, Tokens.Space.Page, 0),
        };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        TopBarContent.Children.Add(mark);

        var profiles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, Tokens.Space.Page, 0) };
        profiles.Children.Add(UiBuild.Muted(new TextBlock { Text = "프로필", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, Tokens.Space.Gap, 0) }));
        _profileButtons = new RadioButton[OverlayProfile.Count];
        for (int i = 0; i < OverlayProfile.Count; i++)
        {
            int n = i;
            var b = Toggle($"{i + 1}", "profile", i == _s.ActiveProfile);
            (b.Margin, b.MinWidth, b.Padding, b.Style) = (new(0, 0, 4, 0), 36, new(10, 4, 10, 4), ProfileButtonStyle);
            b.ToolTip = $"프로필 {i + 1}: 모든 창의 위치·크기와 모양(색, 불투명도, 표시 방식)을 프로필마다 따로 저장합니다.";
            b.Checked += (_, _) => { if (n != _s.ActiveProfile) { RecordStep(); ProfileRequested?.Invoke(n); ShowProfileButtons(); } };
            profiles.Children.Add(_profileButtons[i] = b);
        }
        _testProfile.GroupName = "profile";
        (_testProfile.Margin, _testProfile.Padding, _testProfile.IsHitTestVisible, _testProfile.Focusable, _testProfile.Style) =
            (new(6, 0, 2, 0), new(10, 4, 10, 4), false, false, ProfileButtonStyle);
        _testProfile.ToolTip = "테스트 모드를 켜면 자동으로 이 프로필이 됩니다. 옵션과 위치가 따로 저장되고, 끄면 전에 쓰던 프로필로 돌아갑니다.";
        profiles.Children.Add(_testProfile);
        TopBarContent.Children.Add(profiles);

        // Global, not 편집 중's look; the tray has the same switch (user, 2026-11-17, status/decisions.md). Not a page row, so
        // not in 설정 검색.
        _captureHidden.Content = "방송·캡처에서 숨김";
        _captureHidden.ToolTip = "스크린샷, OBS 방송, 디스코드 화면 공유에 오버레이가 찍히지 않습니다. 내 화면에는 그대로 보입니다. "
            + "끄면 화면 인식도 오버레이를 함께 찍습니다.";
        (_captureHidden.VerticalAlignment, _captureHidden.Margin) = (VerticalAlignment.Center, new(0, 0, Tokens.Space.Page, 0));
        _captureHidden.Click += (_, _) => { _s.Overlay.ExcludeFromCapture = _captureHidden.IsChecked == true; Changed(); };
        ShowCaptureHidden();
        TopBarContent.Children.Add(_captureHidden);

        _context.VerticalAlignment = VerticalAlignment.Center;
        TopBarContent.Children.Add(_context);
        BuildAutoTestBanner();
    }

    /// <summary>The tray's 자동 → 항상 test turned 설정's 자동 parts on outside a raid; 끄기 asks the app to turn it off.</summary>
    public event Action? AutoAsAlwaysOff;

    void BuildAutoTestBanner()
    {
        AutoTestBanner.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
        AutoTestBanner.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        var off = new Button { Content = "끄기", VerticalAlignment = VerticalAlignment.Center, Margin = new(Tokens.Space.Gap, 0, 0, 0) };
        off.Click += (_, _) => AutoAsAlwaysOff?.Invoke();
        DockPanel.SetDock(off, Dock.Right);
        AutoTestBannerContent.Children.Add(off);
        AutoTestBannerContent.Children.Add(new TextBlock
        {
            Text = "수련장 테스트 중: 자동도 항상처럼 표시합니다.",
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        });
    }

    /// <summary>Shows or hides the 수련장 테스트 banner (the app holds the state; the tray has the same switch).</summary>
    public void SetAutoAsAlways(bool on) => AutoTestBanner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The profile buttons as the settings say (switched from the tray or a hotkey, or refused mid-drag).</summary>
    void ShowProfileButtons()
    {
        for (int i = 0; i < _profileButtons.Length; i++)
        {
            bool picked = !_inTestProfile && i == _s.ActiveProfile;
            var btn = _profileButtons[i];
            (btn.IsChecked, btn.IsEnabled) = (picked, !_inTestProfile);
            // The picked one is filled and bold, the others outline only (user, 2026-11-13).
            btn.FontWeight = picked ? FontWeights.Bold : FontWeights.Normal;
        }
        _testProfile.IsChecked = _inTestProfile;
    }

    readonly CheckBox _captureHidden = new();

    /// <summary>The top bar's 방송·캡처에서 숨김 as the settings say. The tray calls only this, not <see cref="Reload"/>: a
    /// reload would close the zoom window and drop its placement.</summary>
    public void ShowCaptureHidden() => _captureHidden.IsChecked = _s.Overlay.ExcludeFromCapture;

    readonly RadioButton _testProfile = new() { Content = "테스트" };
    bool _inTestProfile;

    /// <summary>The live settings are now [Test]'s or the ones before it: everything is shown afresh, and undo can't reach across.</summary>
    public void SettingsSwapped(bool test)
    {
        _inTestProfile = test;
        _history.Clear();
        _lastCopy = null;
        _lastChange = default;
        BuildAll();
        ShowProfileButtons();
        ResetBaseline();
        SettingsChanged?.Invoke();
        LayoutChanged?.Invoke();
    }
}
