using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>설정's colour theme: an option saved with the settings; 딥 틸 is the concept's palette over the dark theme.</summary>
public class UiThemeTests
{
    [Fact]
    public void New_settings_default_to_로스트아크() => Assert.Equal(UiTheme.LostArk, new Settings().Theme);

    /// <summary>A file from before the 로스트아크 theme was saved while 딥 틸 was the default: it stays 딥 틸.</summary>
    [Fact]
    public void A_file_without_a_theme_loads_as_딥_틸()
    {
        string dir = Path.Combine(Path.GetTempPath(), "go-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "Version": 4 }""");
            Assert.Equal(UiTheme.DeepTeal, new SettingsStore(Path.Combine(dir, "settings.json")).Load(out _).Theme);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    public static TheoryData<UiTheme> Themes() => [.. Enum.GetValues<UiTheme>()];

    /// <summary>Every theme that sets its own text colours keeps small text readable on its panels (WCAG 4.5:1).</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_theme_text_passes_contrast(UiTheme theme)
    {
        var c = UiPalette.Of(theme).Colors;
        Rgb At(string key) => HexColor.TryParse(c[key], out var v) ? v : throw new InvalidOperationException(key);
        foreach (var key in new[] { UiPalette.Text, UiPalette.Muted, UiPalette.Gold })
            if (c.ContainsKey(key) && c.ContainsKey(UiPalette.Card))
                Assert.True(HexColor.Contrast(At(key), At(UiPalette.Card)) >= 4.5, $"{theme} {key}: {HexColor.Contrast(At(key), At(UiPalette.Card)):0.00}");
    }

    [Theory]
    [InlineData(UiTheme.LostArk, "#2A2C35", "#C9A472")]
    [InlineData(UiTheme.Graphite, "#25272B", "#8AB4F8")]
    [InlineData(UiTheme.ArkesiaNight, "#1F2433", "#D9B46A")]
    public void The_new_dark_themes_set_their_tokens(UiTheme theme, string card, string mark)
    {
        var p = UiPalette.Of(theme);
        Assert.True(p.Dark);
        Assert.Equal(card, p.Colors[UiPalette.Card]);
        Assert.Contains(mark, p.Colors.Values);
        Assert.All(UiPalette.Keys, k => Assert.Matches("^#[0-9A-F]{6}$", p.Colors[k]));
    }

    [Fact]
    public void 딥_틸_sets_the_concept_colours_over_the_dark_theme()
    {
        var p = UiPalette.Of(UiTheme.DeepTeal);

        Assert.True(p.Dark);
        Assert.Equal("#0D141D", p.Colors[UiPalette.Background]);
        Assert.Equal("#1B2734", p.Colors[UiPalette.Card]);
        Assert.Equal("#83E6E0", p.Colors[UiPalette.Accent]);
        Assert.All(UiPalette.Keys, k => Assert.Matches("^#[0-9A-F]{6}$", p.Colors[k]));
    }

    [Theory]
    [InlineData(UiTheme.System)]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public void The_Fluent_themes_change_no_colours(UiTheme theme)
    {
        Assert.Empty(UiPalette.Of(theme).Colors);
    }

    [Fact]
    public void The_theme_is_saved()
    {
        string dir = Path.Combine(Path.GetTempPath(), "go-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            store.Save(new Settings { Theme = UiTheme.Light });

            Assert.Equal(UiTheme.Light, store.Load(out _).Theme);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
