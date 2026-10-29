namespace GaugeOverlay.Core.Domain;

/// <summary>설정's look: Windows' own (<see cref="System"/>, follows the Windows light/dark choice), Fluent light or dark, or a
/// palette of the app's over the dark theme: 로스트아크 (black, white and gold from the official site), 딥 틸, 그래파이트,
/// 아크라시아 밤. The name is what a settings file stores; values after <see cref="Dark"/>, which 0.11.x doesn't know, are saved in Settings.Palette.</summary>
public enum UiTheme { DeepTeal, System, Light, Dark, LostArk, Graphite, ArkesiaNight }

/// <summary>A theme's colours: whether it is dark, and the colours it sets over Fluent's (none for the Fluent themes).
/// <see cref="Text"/>, <see cref="Muted"/> and <see cref="Gold"/> only where the theme sets its own text colours.</summary>
public sealed record UiPalette(bool Dark, IReadOnlyDictionary<string, string> Colors)
{
    public const string Background = "Background", Nav = "Nav", Card = "Card", Stroke = "Stroke", Accent = "Accent", OnAccent = "OnAccent";
    public const string Text = "Text", Muted = "Muted", Gold = "Gold";
    public static readonly string[] Keys = [Background, Nav, Card, Stroke, Accent, OnAccent];

    public static UiPalette Of(UiTheme theme) => theme switch
    {
        UiTheme.DeepTeal => new(true, new Dictionary<string, string>
        {
            [Background] = "#0D141D", [Nav] = "#151E29", [Card] = "#1B2734", [Stroke] = "#2A3A4C",
            [Accent] = "#83E6E0", [OnAccent] = "#0D141D",
        }),
        // Switches and sliders in gold (on / important); text white and warm grey on the official tooltip slate.
        UiTheme.LostArk => new(true, new Dictionary<string, string>
        {
            [Background] = "#18191D", [Nav] = "#18191D", [Card] = "#2A2C35", [Stroke] = "#1C1F2A",
            [Accent] = "#C9A472", [OnAccent] = "#18191D", [Text] = "#F1EEE8", [Muted] = "#B3AFA8", [Gold] = "#C9A472",
        }),
        UiTheme.Graphite => new(true, new Dictionary<string, string>
        {
            [Background] = "#1C1D20", [Nav] = "#202124", [Card] = "#25272B", [Stroke] = "#34363C",
            [Accent] = "#8AB4F8", [OnAccent] = "#1C1D20", [Text] = "#ECECEE", [Muted] = "#9A9CA3",
        }),
        UiTheme.ArkesiaNight => new(true, new Dictionary<string, string>
        {
            [Background] = "#161A26", [Nav] = "#1A1F2D", [Card] = "#1F2433", [Stroke] = "#2E3446",
            [Accent] = "#D9B46A", [OnAccent] = "#161A26", [Text] = "#EDE7DA", [Muted] = "#A39C8E",
        }),
        UiTheme.Light => new(false, new Dictionary<string, string>()),
        _ => new(theme == UiTheme.Dark, new Dictionary<string, string>()),
    };
}
