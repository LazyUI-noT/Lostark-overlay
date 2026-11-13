using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>A buff bar is 끄기 or 켜기; one 자동 for every bar shows them only in a raid (the in-game meter or a boss bar
/// seen, RaidPresence) (user, 2026-11-12).</summary>
public class BuffShowTests
{
    [Fact]
    public void A_bar_is_on_by_default_and_a_settings_file_from_before_stays_as_it_was()
    {
        Assert.True(new BuffSettings().Of(BuffKind.공증).Visible);
        Assert.False(new BuffSettings().RaidOnly);

        var old = JsonSerializer.Deserialize<BuffIndicator>("""{"Visible":false}""", SettingsStore.Json)!;
        Assert.False(old.Visible);
    }

    [Fact]
    public void 자동_hides_a_bar_only_outside_a_raid()
    {
        var b = new BuffSettings { RaidOnly = true };

        Assert.True(b.OutOfRaid(inDungeon: false));
        Assert.False(b.OutOfRaid(inDungeon: true));
        Assert.False(new BuffSettings().OutOfRaid(inDungeon: false));   // 자동 끄기
    }

    [Fact]
    public void Where_the_player_is_only_needs_watching_with_자동_and_a_bar_shown()
    {
        var b = new BuffSettings();
        Assert.False(b.AnyInRaidOnly);

        b.RaidOnly = true;
        Assert.True(b.AnyInRaidOnly);

        foreach (var k in Enum.GetValues<BuffKind>()) b.Edit(k).Visible = false;
        Assert.False(b.AnyInRaidOnly);
    }

    [Theory]
    [InlineData("""{"Indicators":{"공증":{"InRaidOnly":true}}}""", true)]
    [InlineData("""{"Indicators":{"공증":{"Visible":false,"InRaidOnly":true}}}""", true)]
    [InlineData("""{"Indicators":{"공증":{"Visible":true}}}""", false)]
    [InlineData("""{"RaidOnly":false,"Indicators":{"공증":{"InRaidOnly":true}}}""", true)]
    public void An_older_file_with_any_bar_on_자동_turns_자동_on_for_all(string json, bool raidOnly)
    {
        var b = JsonSerializer.Deserialize<BuffSettings>(json, SettingsStore.Json)!;

        b.Migrate();

        Assert.Equal(raidOnly, b.RaidOnly);
        Assert.False(b.Of(BuffKind.공증).InRaidOnly);   // moved, so a later 자동 끄기 sticks
    }

    [Fact]
    public void Loading_a_settings_file_moves_the_old_per_bar_자동()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{"Buffs":{"Indicators":{"아덴피증":{"InRaidOnly":true}}}}""", SettingsStore.Json)!;

        SettingsStore.Normalize(s);

        Assert.True(s.Buffs.RaidOnly);
    }

    [Fact]
    public void The_tray_off_and_on_keeps_자동()
    {
        var s = new Settings();
        s.Buffs.RaidOnly = true;

        TrayToggles.Show(s, OverlayPart.Buffs, false);   // hidden for the character played; 자동 is everyone's and stays
        Assert.True(s.Buffs.RaidOnly);
        Assert.False(ClassProfiles.BuffShown(s, BuffKind.공증));

        TrayToggles.Show(s, OverlayPart.Buffs, true);
        Assert.True(s.Buffs.RaidOnly);
    }
}
