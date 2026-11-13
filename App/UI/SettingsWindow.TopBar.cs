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
            Text = "GaugeOverlay", FontFamily = Themes.Cinzel, FontSize = 20, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 32, 0),
        };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        TopBarContent.Children.Add(mark);

        var profiles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 32, 0) };
        profiles.Children.Add(UiBuild.Muted(new TextBlock { Text = "프로필", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) }));
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

        _context.VerticalAlignment = VerticalAlignment.Center;
        TopBarContent.Children.Add(_context);
    }

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

    readonly RadioButton _testProfile = new() { Content = "Test" };
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
