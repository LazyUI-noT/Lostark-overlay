using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;
using Microsoft.Win32;

namespace GaugeOverlay.App.UI;

/// <summary>설정's theme (테마) on any of the app's windows: 설정, 시작하기 and the party dialogs, so they all look alike.
/// Fluent light, dark or Windows', then the palette's own colours over it (딥 틸).</summary>
public static class Themes
{
    /// <summary>The window's own backgrounds: unset (the Fluent window shows through) unless the theme gives them.</summary>
    public const string BackgroundBrushKey = "SettingsBackgroundBrush", NavBrushKey = "SettingsNavBrush";
    public const string CardBrushKey = "CardBackgroundFillColorDefaultBrush", StrokeBrushKey = "CardStrokeColorDefaultBrush";

    /// <summary>The app's own state colours, set in every theme (<see cref="UiPalette.States"/>): not Fluent's keys, which
    /// Fluent's own controls also draw with.</summary>
    public const string DisabledBrushKey = "SettingsDisabledBrush", DangerBrushKey = "SettingsDangerBrush";

    /// <summary>Which resources each palette colour sets: the window's own, and the Fluent ones the controls draw with.
    /// The Fluent styles read the control keys (ToggleButtonBackgroundChecked…) dynamically but the Accent* brushes
    /// they point at statically, so the accent is set on the control keys.</summary>
    static readonly (string Palette, string[] Brushes)[] Brushes =
    [
        (UiPalette.Background, [BackgroundBrushKey]),
        (UiPalette.Nav, [NavBrushKey]),
        (UiPalette.Card, [CardBrushKey]),
        (UiPalette.Stroke, [StrokeBrushKey]),
        (UiPalette.Accent,
        [
            "AccentFillColorDefaultBrush", "AccentTextFillColorPrimaryBrush",
            "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed",
            "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke", "RadioButtonOuterEllipseCheckedStrokePointerOver",
            "RadioButtonCheckOuterEllipseCheckedFillPointerOver", "RadioButtonCheckOuterEllipseCheckedFillPressed",
            "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "AccentButtonBackground", "AccentButtonBackgroundPointerOver",
            "TextControlFocusedBorderBrush", "ComboBoxItemPillFillBrush",
        ]),
        (UiPalette.Text, ["TextFillColorPrimaryBrush"]),
        (UiPalette.Muted, ["TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush"]),
        (UiPalette.OnAccent, ["TextOnAccentFillColorPrimaryBrush", "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPressed", "AccentButtonForeground"]),
    ];

    /// <summary>The settings windows' text font (bundled, OFL); 맑은 고딕 where a glyph is missing.</summary>
    public static readonly FontFamily Pretendard = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Pretendard, Malgun Gothic");

    /// <summary>The wordmark's font (bundled, OFL): Roman capitals like the official site's titles.</summary>
    public static readonly FontFamily Cinzel = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Cinzel");

    /// <summary>Sets <paramref name="theme"/> on <paramref name="window"/>; <paramref name="root"/>, when given, takes the
    /// theme's background.</summary>
    public static void Apply(Window window, UiTheme theme, Panel? root = null)
    {
        // Windows' high contrast: Fluent draws the system colours itself; the palette's own colours would hide them.
        if (SystemParameters.HighContrast) theme = UiTheme.System;
        var palette = UiPalette.Of(theme);
#pragma warning disable WPF0001   // Fluent theme
        window.ThemeMode = theme == UiTheme.System ? ThemeMode.System : palette.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        foreach (var (key, brushes) in Brushes)
            foreach (var brush in brushes)
                if (palette.Colors.TryGetValue(key, out var hex) && UiColor.TryParse(hex) is { } c)
                    window.Resources[brush] = new SolidColorBrush(c);
                else window.Resources.Remove(brush);
        bool dark = theme == UiTheme.System ? AppsDark() : palette.Dark;
        SetStates(window.Resources, theme, dark);
        FeatureMapView.SetTheme(window.Resources, dark);
        window.FontFamily = Pretendard;
        root?.SetResourceReference(Panel.BackgroundProperty, BackgroundBrushKey);
    }

    /// <summary>Fluent's keys for disabled text (all Fluent's TextFillColorDisabled, white or black at 36 %: too faint, system.md
    /// "States"). The styles read these dynamically, so setting them here reaches Fluent's own disabled controls.</summary>
    static readonly string[] FluentDisabledText =
    [
        "TextFillColorDisabledBrush", "ButtonForegroundDisabled", "RepeatButtonForegroundDisabled", "ToggleButtonForegroundDisabled",
        "HyperlinkButtonForegroundDisabled", "HyperlinkForegroundDisabled", "CardForegroundDisabled", "CheckBoxForegroundUncheckedDisabled",
        "RadioButtonForegroundDisabled", "ComboBoxForegroundDisabled", "ComboBoxDropDownGlyphForegroundDisabled", "TextControlForegroundDisabled",
        "ToggleSwitchContentForegroundDisabled", "ExpanderHeaderDisabledForeground", "DataGridHeaderForegroundDisabled",
        "FrameMenuItemForegroundDisabled", "StatusBarItemForegroundDisabled", "TimePickerButtonForegroundDisabled",
    ];

    /// <summary>Disabled and Danger: the theme's own, or in Windows' high contrast the system's colours, which Fluent's keys
    /// are then left to (as the palette's are).</summary>
    static void SetStates(ResourceDictionary resources, UiTheme theme, bool dark)
    {
        if (SystemParameters.HighContrast)
        {
            resources[DisabledBrushKey] = SystemColors.GrayTextBrush;
            resources[DangerBrushKey] = SystemColors.WindowTextBrush;
            foreach (var key in FluentDisabledText) resources.Remove(key);
            return;
        }
        var (disabled, danger) = UiPalette.States(theme, dark);
        var disabledBrush = Frozen(UiColor.Parse(disabled, Colors.Gray));
        resources[DisabledBrushKey] = disabledBrush;
        resources[DangerBrushKey] = Frozen(UiColor.Parse(danger, Colors.IndianRed));
        foreach (var key in FluentDisabledText) resources[key] = disabledBrush;
    }

    /// <summary>Calls <paramref name="reapply"/> when Windows' light/dark or colours change while <paramref name="window"/> is
    /// open: 테마 = Windows 설정 따름 resolves Disabled, Danger and the map's colours at <see cref="Apply"/>, not live.</summary>
    public static void FollowWindows(Window window, Action reapply)
    {
        void OnChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            // Light/dark arrives as General (ImmersiveColorSet); high contrast as Color.
            if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color)
                window.Dispatcher.BeginInvoke(reapply);
        }
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnChanged;   // a static event: 설정 only hides, so this lasts until the app exits (one window, cheap)
        window.Closed += (_, _) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnChanged;
    }

    static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    /// <summary>Windows' "앱 모드: 어둡게" (Settings → 개인 설정 → 색), which "Windows 설정 따름" follows. Unreadable: light.</summary>
    public static bool AppsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>The accent ("primary") button style, for the one action a window or step is for.</summary>
    public static void Primary(Button b) => b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
}
