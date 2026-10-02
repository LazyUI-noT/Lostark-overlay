namespace GaugeOverlay.Core.Domain;

/// <summary>설정's look: Windows' own (<see cref="UiTheme.System"/>, follows the Windows light/dark choice), Fluent light or dark,
/// or 딥 틸, the settings concept's palette over the dark theme.</summary>
public enum UiTheme { DeepTeal, System, Light, Dark }

/// <summary>A theme's colours: whether it is dark, and the colours it sets over Fluent's (none for the Fluent themes).</summary>
public sealed record UiPalette(bool Dark, IReadOnlyDictionary<string, string> Colors)
{
    public const string Background = "Background", Nav = "Nav", Card = "Card", Stroke = "Stroke", Accent = "Accent", OnAccent = "OnAccent";
    public static readonly string[] Keys = [Background, Nav, Card, Stroke, Accent, OnAccent];

    public static UiPalette Of(UiTheme theme) => theme switch
    {
        UiTheme.DeepTeal => new(true, new Dictionary<string, string>
        {
            [Background] = "#0D141D", [Nav] = "#151E29", [Card] = "#1B2734", [Stroke] = "#2A3A4C",
            [Accent] = "#83E6E0", [OnAccent] = "#0D141D",
        }),
        UiTheme.Light => new(false, new Dictionary<string, string>()),
        _ => new(theme == UiTheme.Dark, new Dictionary<string, string>()),
    };
}
