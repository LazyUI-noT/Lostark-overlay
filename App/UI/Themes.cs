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
        FeatureMapView.SetTheme(window.Resources, theme == UiTheme.System ? AppsDark() : palette.Dark);
        window.FontFamily = Pretendard;
        root?.SetResourceReference(Panel.BackgroundProperty, BackgroundBrushKey);
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
