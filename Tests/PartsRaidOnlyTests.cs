using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>게이지 and 스킬 each have a 자동 like the buff bars': shown only in a raid (user, 2026-11-15).</summary>
public class PartsRaidOnlyTests
{
    [Fact]
    public void 자동_is_off_by_default_and_in_an_older_file()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{"Gauge":{"Fill":"#FFFFFF"},"Overlay":{"Opacity":0.5}}""", SettingsStore.Json)!;

        Assert.False(s.Gauge.RaidOnly);
        Assert.False(s.Overlay.SkillsRaidOnly);
        Assert.False(s.AnyInRaidOnly);
    }

    [Fact]
    public void Gauge_자동_hides_it_only_outside_a_raid()
    {
        var g = new GaugeStyle { RaidOnly = true };

        Assert.True(g.OutOfRaid(inDungeon: false));
        Assert.False(g.OutOfRaid(inDungeon: true));
        Assert.False(new GaugeStyle().OutOfRaid(inDungeon: false));
    }

    [Fact]
    public void Skills_자동_hides_them_only_outside_a_raid()
    {
        var o = new OverlaySettings { SkillsRaidOnly = true };

        Assert.True(o.SkillsOutOfRaid(inDungeon: false));
        Assert.False(o.SkillsOutOfRaid(inDungeon: true));
        Assert.False(new OverlaySettings().SkillsOutOfRaid(inDungeon: false));
    }

    [Fact]
    public void Any_자동_makes_the_engine_watch_where_the_player_is()
    {
        var s = new Settings();
        s.Gauge.RaidOnly = true;
        Assert.True(s.AnyInRaidOnly);

        s = new Settings();
        s.Overlay.SkillsRaidOnly = true;
        Assert.True(s.AnyInRaidOnly);

        s = new Settings();
        s.Buffs.RaidOnly = true;
        Assert.True(s.AnyInRaidOnly);
    }

    [Fact]
    public void Judgment_자동_hides_it_only_outside_a_raid_and_counts_for_the_active_character_only()
    {
        var j = new JudgmentSettings { RaidOnly = true };
        Assert.True(j.OutOfRaid(inDungeon: false));
        Assert.False(j.OutOfRaid(inDungeon: true));
        Assert.False(new JudgmentSettings().OutOfRaid(inDungeon: false));

        var s = new Settings { ActiveCharacter = "a" };
        s.Judgment.RaidOnly = true;
        Assert.False(s.AnyInRaidOnly);   // 단심 off for this character: nothing to watch for
        s.Judgment.Set("a", true);
        Assert.True(s.AnyInRaidOnly);
    }

    [Fact]
    public void The_tray_test_shows_자동_parts_anywhere_and_a_raid_shows_them_without_it()
    {
        var now = TimeSpan.FromSeconds(100);
        var town = Snapshot.Empty(now);
        var raid = town with { Presence = RaidPresence.None.Saw(meter: true, boss: false, now) };

        Assert.False(town.AutoShows(now));
        Assert.True((town with { AutoAsAlways = true }).AutoShows(now));
        Assert.True(raid.AutoShows(now));
    }

    [Fact]
    public void The_switches_survive_a_save()
    {
        var s = new Settings();
        s.Gauge.RaidOnly = true;
        s.Overlay.SkillsRaidOnly = true;
        s.Judgment.RaidOnly = true;

        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, SettingsStore.Json), SettingsStore.Json)!;

        Assert.True(back.Gauge.RaidOnly);
        Assert.True(back.Overlay.SkillsRaidOnly);
        Assert.True(back.Judgment.RaidOnly);
        Assert.False(JsonSerializer.Deserialize<Settings>("{}", SettingsStore.Json)!.Judgment.RaidOnly);
    }
}
