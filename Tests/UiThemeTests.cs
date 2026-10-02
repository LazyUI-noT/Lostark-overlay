using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>설정's colour theme: an option saved with the settings; 딥 틸 is the concept's palette over the dark theme.</summary>
public class UiThemeTests
{
    [Fact]
    public void The_default_theme_is_딥_틸()
    {
        Assert.Equal(UiTheme.DeepTeal, new Settings().Theme);
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
