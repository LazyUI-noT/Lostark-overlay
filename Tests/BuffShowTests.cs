using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>A buff bar's 표시: 끄기, 켜기, 자동 (only in a raid: the in-game meter or a boss bar seen, RaidPresence).</summary>
public class BuffShowTests
{
    [Fact]
    public void A_bar_is_on_by_default_and_a_settings_file_from_before_stays_as_it_was()
    {
        Assert.Equal(BuffShow.켜기, new BuffSettings().Of(BuffKind.공증).Show);

        var old = JsonSerializer.Deserialize<BuffIndicator>("""{"Visible":false}""", SettingsStore.Json)!;
        Assert.Equal(BuffShow.끄기, old.Show);
        var on = JsonSerializer.Deserialize<BuffIndicator>("""{"Visible":true}""", SettingsStore.Json)!;
        Assert.Equal(BuffShow.켜기, on.Show);
    }

    [Theory]
    [InlineData(BuffShow.끄기, false, false)]
    [InlineData(BuffShow.켜기, true, false)]
    [InlineData(BuffShow.자동, true, true)]
    public void Each_mode_is_kept_in_the_saved_properties(BuffShow show, bool visible, bool inRaidOnly)
    {
        var i = new BuffIndicator { Show = show };
        Assert.Equal((visible, inRaidOnly), (i.Visible, i.InRaidOnly));

        var back = JsonSerializer.Deserialize<BuffIndicator>(JsonSerializer.Serialize(i, SettingsStore.Json), SettingsStore.Json)!;
        Assert.Equal(show, back.Show);
    }

    [Fact]
    public void Only_a_자동_bar_outside_a_raid_is_out_of_raid()
    {
        var b = new BuffSettings();
        b.Edit(BuffKind.공증).Show = BuffShow.자동;

        Assert.True(b.OutOfRaid(BuffKind.공증, inDungeon: false));
        Assert.False(b.OutOfRaid(BuffKind.공증, inDungeon: true));
        Assert.False(b.OutOfRaid(BuffKind.뎀증, inDungeon: false));   // 켜기
    }

    [Fact]
    public void Where_the_player_is_only_needs_watching_for_a_자동_bar_that_is_shown()
    {
        var b = new BuffSettings();
        Assert.False(b.AnyInRaidOnly);

        b.Edit(BuffKind.공증).Show = BuffShow.자동;
        Assert.True(b.AnyInRaidOnly);

        b.Edit(BuffKind.공증).Visible = false;   // the tray's 표시 off
        Assert.False(b.AnyInRaidOnly);
    }

    [Fact]
    public void The_tray_off_and_on_brings_자동_back()
    {
        var s = new Settings();
        s.Buffs.Edit(BuffKind.공증).Show = BuffShow.자동;

        TrayToggles.Show(s, OverlayPart.Buffs, false);   // hidden for the character played; 자동 is everyone's and stays
        Assert.Equal(BuffShow.자동, s.Buffs.Of(BuffKind.공증).Show);
        Assert.False(ClassProfiles.BuffShown(s, BuffKind.공증));

        TrayToggles.Show(s, OverlayPart.Buffs, true);
        Assert.Equal(BuffShow.자동, s.Buffs.Of(BuffKind.공증).Show);
    }
}
