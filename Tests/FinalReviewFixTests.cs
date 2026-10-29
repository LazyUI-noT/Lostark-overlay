using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The final review's fixes: a file 0.11.x can still open, undo keeping screenshots, tray 표시 keeping choices.</summary>
public class FinalReviewFixTests
{
    static string Save(Settings s) => JsonSerializer.Serialize(s, SettingsStore.Json);
    static Settings Load(string json) => JsonSerializer.Deserialize<Settings>(json, SettingsStore.Json)!;

    [Theory]
    [InlineData(UiTheme.LostArk)]
    [InlineData(UiTheme.Graphite)]
    [InlineData(UiTheme.ArkesiaNight)]
    public void A_new_theme_saves_an_old_value_under_Theme_and_comes_back(UiTheme theme)
    {
        string json = Save(new Settings { Theme = theme });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("DeepTeal", doc.RootElement.GetProperty("Theme").GetString());   // what 0.11.x can read
        Assert.Equal(theme, Load(json).Theme);
    }

    [Fact]
    public void An_old_theme_round_trips_and_a_file_from_0_11_keeps_its_theme()
    {
        Assert.Equal(UiTheme.Dark, Load(Save(new Settings { Theme = UiTheme.Dark })).Theme);
        Assert.Equal(UiTheme.Light, Load("""{ "Theme": "Light" }""").Theme);
    }

    [Fact]
    public void Undo_keeps_the_screenshot_area_each_profile_has_now()
    {
        var area = new PixelRect(0, 0, 1920, 1080);
        var live = new Settings();
        live.Profiles[1].Shot = area;   // taken after the snapshot: a file, no undo step

        live.CopyFrom(new Settings());

        Assert.Equal(area, live.Profiles[1].Shot);
    }

    [Fact]
    public void Skills_off_then_on_brings_back_only_the_groups_that_were_shown()
    {
        var s = new Settings();
        var key = ClassProfiles.ActiveKey(s);   // what the tray edits: the character shown (기본 here)
        ClassProfiles.Edit(s, key).Mine.Groups[SlotGroup.각성기] = false;

        TrayToggles.Show(s, OverlayPart.Skills, false);
        TrayToggles.Show(s, OverlayPart.Skills, true);

        var groups = ClassProfiles.Of(s, key).Mine.Groups;
        Assert.False(groups[SlotGroup.각성기]);
        Assert.All(Enum.GetValues<SlotGroup>().Where(g => g != SlotGroup.각성기), g => Assert.True(groups[g]));
    }

    [Fact]
    public void Buffs_off_then_on_brings_back_only_the_bars_that_were_shown()
    {
        var s = new Settings();
        foreach (var k in Enum.GetValues<BuffKind>()) s.Buffs.Edit(k).Visible = k is BuffKind.공증 or BuffKind.뎀증;

        TrayToggles.Show(s, OverlayPart.Buffs, false);
        TrayToggles.Show(s, OverlayPart.Buffs, true);

        Assert.All(Enum.GetValues<BuffKind>(), k => Assert.Equal(k is BuffKind.공증 or BuffKind.뎀증, s.Buffs.Of(k).Visible));
    }

    [Fact]
    public void Undo_leaves_the_theme_as_it_is()
    {
        var live = new Settings { Theme = UiTheme.Graphite };

        live.CopyFrom(new Settings { Theme = UiTheme.Dark });

        Assert.Equal(UiTheme.Graphite, live.Theme);
    }
}
