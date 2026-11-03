using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 → 레이드 (RaidSettings): off until turned on, kept through the file, and the old 모르둠 debuff switch carried over.</summary>
public class RaidSettingsTests
{
    static readonly RaidGate Mordum = new("act3-g3", "3막", "3관문", ["심연의 징벌자, 모르둠"], [750, 300], Raid.모르둠);

    static Settings RoundTrip(Settings s) => SettingsStore.Normalize(
        JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(s, SettingsStore.Json), SettingsStore.Json)!);

    [Fact]
    public void Everything_is_off_by_default()
    {
        var r = new Settings().Raids;
        Assert.True(r.Auto);
        Assert.False(r.Gate("act3-g3").CountWindow);
        Assert.True(r.DebuffsAuto);   // 디버프 창 자동, every raid
        Assert.Equal((GlowUntil.Seconds, 20, 5), (r.GlowUntil, r.GlowSeconds, r.GlowBars));
    }

    [Fact]
    public void Gate_settings_survive_a_save_and_a_load()
    {
        var s = new Settings();
        var g = s.Raids.Gate("act3-g3");
        (g.CountWindow, g.Alerts, g.Offset) = (true, [700, 200], 3);
        s.Raids.Look = BarCountLook.화면그대로;
        var back = RoundTrip(s).Raids;
        Assert.True(back.Gate("act3-g3").CountWindow);
        Assert.Equal([700, 200], back.Gate("act3-g3").Alerts!);
        Assert.Equal(BarCountLook.화면그대로, back.Look);
    }

    [Fact]
    public void Old_모르둠_debuff_raid_before_the_table_stays_the_tray_pick()
    {
        var s = new Settings();
        s.Debuffs.Raid = Raid.모르둠;
        s.Raids.Migrated = false;   // a file saved before the 레이드 table
        var back = RoundTrip(s);
        Assert.Equal(Raid.모르둠, back.Debuffs.Raid);   // shown by hand in any dungeon, as before
        Assert.True(back.Raids.DebuffsMigrated);
    }

    // 0.13–0.16.0: the 디버프 창 per gate, mirrored into Debuffs.Raid (자동 or not). Now: one 자동 switch and the tray's pick.
    [Theory]
    [InlineData(true, false, "모르둠", false)]   // always shown → the tray's pick
    [InlineData(true, true, "없음", true)]       // 자동 → 디버프 창 자동
    [InlineData(false, true, "없음", false)]     // off stays off (user, 2026-11-03)
    public void The_per_gate_debuff_window_becomes_the_one_switch_and_the_tray_pick(bool window, bool auto, string picked, bool debuffsAuto)
    {
        var s = new Settings();
        (s.Raids.Gate(RaidSettings.MordumGate).DebuffWindow, s.Raids.Gate(RaidSettings.MordumGate).DebuffAuto) = (window, auto);
        s.Debuffs.Raid = window ? Raid.모르둠 : Raid.없음;   // as SyncDebuffRaid mirrored it
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(s, SettingsStore.Json))!.AsObject();
        node["Raids"]!.AsObject().Remove("DebuffsMigrated");   // as 0.16.0 saved it
        node["Raids"]!.AsObject().Remove("DebuffsAuto");
        var back = SettingsStore.Normalize(node.Deserialize<Settings>(SettingsStore.Json)!);
        Assert.Equal(Enum.Parse<Raid>(picked), back.Debuffs.Raid);
        Assert.Equal(debuffsAuto, back.Raids.DebuffsAuto);
        Assert.False(back.Raids.Gate(RaidSettings.MordumGate).DebuffWindow);   // read once, then left off
    }

    [Fact]
    public void A_new_install_keeps_디버프_창_자동_on_after_a_save_and_a_load()
    {
        var back = RoundTrip(new Settings());   // saved with DebuffsMigrated: nothing to carry over
        Assert.True(back.Raids.DebuffsAuto);
        Assert.True(RoundTrip(back).Raids.DebuffsAuto);
    }

    [Fact]
    public void 디버프_창_자동_turned_off_stays_off_after_a_load()
    {
        var s = new Settings();
        s.Raids.DebuffsAuto = false;
        Assert.False(RoundTrip(s).Raids.DebuffsAuto);
    }

    [Fact]
    public void A_file_with_no_Raids_key_keeps_its_tray_pick()
    {
        var old = new Settings();
        old.Debuffs.Raid = Raid.모르둠;
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(old, SettingsStore.Json))!.AsObject();
        node.Remove("Raids");   // as 0.12 saved it
        var loaded = SettingsStore.Normalize(node.Deserialize<Settings>(SettingsStore.Json)!);
        Assert.Equal(Raid.모르둠, loaded.Debuffs.Raid);
        Assert.Equal(Raid.없음, SettingsStore.Normalize(new Settings()).Debuffs.Raid);
    }

    [Fact]
    public void The_migration_runs_once()
    {
        var s = new Settings();
        s.Debuffs.Raid = Raid.모르둠;
        s.Raids.Migrated = false;
        s = RoundTrip(s);
        s.Debuffs.Raid = Raid.없음;   // the user turns it off afterwards
        Assert.Equal(Raid.없음, RoundTrip(s).Debuffs.Raid);
    }

    [Fact]
    public void Alerts_fall_back_to_the_gate_defaults_until_edited()
    {
        var r = new RaidSettings();
        Assert.Equal([750, 300], r.AlertsOf(Mordum));
        r.Gate(Mordum.Id).Alerts = [500];
        Assert.Equal([500], r.AlertsOf(Mordum));
        r.Gate(Mordum.Id).Alerts = [];   // emptied by hand: no alerts, not the defaults
        Assert.Empty(r.AlertsOf(Mordum));
    }

    [Fact]
    public void The_offset_is_shared_unless_per_gate()
    {
        var r = new RaidSettings { Offset = 5 };
        r.Gate("a").Offset = -2;
        Assert.Equal(5, r.OffsetOf("a"));
        r.OffsetPerGate = true;
        Assert.Equal(-2, r.OffsetOf("a"));
        Assert.Equal(0, r.OffsetOf("b"));
    }

    [Fact]
    public void Hand_edited_numbers_are_clamped()
    {
        var s = new Settings();
        (s.Raids.GlowSeconds, s.Raids.GlowBars, s.Raids.Opacity) = (-4, 100000, 7);
        var r = RoundTrip(s).Raids;
        Assert.Equal((1, RaidSettings.MaxGlowBars, 1.0), (r.GlowSeconds, r.GlowBars, r.Opacity));
    }

    [Fact]
    public void A_clone_is_equal_and_apart()
    {
        var r = new RaidSettings { Offset = 4, Look = BarCountLook.화면그대로 };
        r.Gate("act3-g3").Alerts = [700];
        var c = r.Clone();
        Assert.Equal((4, BarCountLook.화면그대로), (c.Offset, c.Look));
        Assert.Equal([700], c.Gate("act3-g3").Alerts!);
        r.Gate("act3-g3").Alerts!.Add(1);
        r.Gate("x").CountWindow = true;
        Assert.Equal([700], c.Gate("act3-g3").Alerts!);
        Assert.False(c.Gates.ContainsKey("x"));
    }

    [Fact]
    public void The_windows_shown_follow_the_switches()
    {
        var s = new Settings();
        Assert.False(s.Raids.CountShown());
        Assert.True(s.Raids.DebuffShown(s.Debuffs));   // 디버프 창 자동
        s.Raids.Gate("act3-g3").CountWindow = true;
        Assert.True(s.Raids.CountShown());
        s.Raids.DebuffsAuto = false;
        Assert.False(s.Raids.DebuffShown(s.Debuffs));
        s.Debuffs.Raid = Raid.모르둠;   // picked in the tray
        Assert.True(s.Raids.DebuffShown(s.Debuffs));
    }

    [Fact]
    public void The_main_줄_수_switch_hides_the_count_window_and_keeps_each_gate_on_a_reload()
    {
        var s = new Settings();
        s.Raids.Gate("act3-g3").CountWindow = true;
        s.Raids.CountEnabled = false;
        Assert.False(s.Raids.CountShown());
        Assert.False(s.Raids.CountOn("act3-g3"));
        var back = RoundTrip(s).Raids;
        Assert.False(back.CountEnabled);
        Assert.True(back.Gate("act3-g3").CountWindow);
    }

    [Fact]
    public void A_file_saved_before_the_main_줄_수_switch_loads_it_on()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(new Settings(), SettingsStore.Json))!.AsObject();
        node["Raids"]!.AsObject().Remove("CountEnabled");
        Assert.True(SettingsStore.Normalize(node.Deserialize<Settings>(SettingsStore.Json)!).Raids.CountEnabled);
    }

    [Fact]
    public void The_helpers_are_not_saved()
    {
        var json = JsonSerializer.Serialize(new RaidSettings(), SettingsStore.Json);
        Assert.DoesNotContain("CountShown", json);
        Assert.DoesNotContain("DebuffShown", json);
    }

    [Fact]
    public void The_two_three_estimate_settings_survive_a_save_and_hand_edits_are_clamped()
    {
        var s = new Settings();
        Assert.Equal((43.5, 1.0), (s.Raids.AssistShare, s.Raids.OtherParty));
        (s.Raids.AssistShare, s.Raids.OtherParty) = (45, 0.5);
        s.Raids.Gate("final-ex-g2-3").Esthers = [new("공아만 3칸", 1870, 60), new("히든 아만 3칸", 3779, 10)];
        (s.Raids.Gate("final-ex-g2-3").Assist1, s.Raids.Gate("final-ex-g2-3").Assist2) = (1000, 1100);
        Assert.Equal((1000, 1100), (RoundTrip(s).Raids.Gate("final-ex-g2-3").Assist1, RoundTrip(s).Raids.Gate("final-ex-g2-3").Assist2));
        var back = RoundTrip(s).Raids;
        Assert.Equal((45, 0.5), (back.AssistShare, back.OtherParty));
        Assert.Equal([new EstherUse("공아만 3칸", 1870, 60), new EstherUse("히든 아만 3칸", 3779, 10)], back.Gate("final-ex-g2-3").Esthers);
        (s.Raids.AssistShare, s.Raids.OtherParty) = (500, -1);
        s.Raids.Gate("final-ex-g2-3").Esthers = [new("x", -5, -1), new(" ", 10, 10)];
        back = RoundTrip(s).Raids;
        Assert.Equal((RaidSettings.MaxAssistShare, 0), (back.AssistShare, back.OtherParty));
        Assert.Equal([new EstherUse("x", 0, 0)], back.Gate("final-ex-g2-3").Esthers);   // clamped; a nameless use dropped
    }
}
