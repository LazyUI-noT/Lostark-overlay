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
        Assert.False(r.Gate("act3-g3").DebuffWindow);
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
    public void Old_모르둠_debuff_raid_turns_on_the_gate_debuff_window_always_shown()
    {
        var s = new Settings();
        s.Debuffs.Raid = Raid.모르둠;
        s.Raids.Migrated = false;   // a file saved before the 레이드 table
        var g = RoundTrip(s).Raids.Gate(RaidSettings.MordumGate);
        Assert.True(g.DebuffWindow);
        Assert.False(g.DebuffAuto);   // as before: shown without waiting for 자동 인식
    }

    [Fact]
    public void A_file_with_no_Raids_key_is_migrated_and_a_new_install_is_not()
    {
        var old = new Settings();
        old.Debuffs.Raid = Raid.모르둠;
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(old, SettingsStore.Json))!.AsObject();
        node.Remove("Raids");   // as 0.12 saved it
        var loaded = SettingsStore.Normalize(node.Deserialize<Settings>(SettingsStore.Json)!);
        Assert.True(loaded.Raids.Gate(RaidSettings.MordumGate).DebuffWindow);
        Assert.True(loaded.Raids.Migrated);

        var fresh = SettingsStore.Normalize(new Settings());
        Assert.False(fresh.Raids.Gate(RaidSettings.MordumGate).DebuffWindow);
    }

    [Fact]
    public void The_migration_runs_once()
    {
        var s = new Settings();
        s.Debuffs.Raid = Raid.모르둠;
        s.Raids.Migrated = false;
        s = RoundTrip(s);
        s.Raids.Gate(RaidSettings.MordumGate).DebuffWindow = false;   // the user turns it off afterwards
        Assert.False(RoundTrip(s).Raids.Gate(RaidSettings.MordumGate).DebuffWindow);
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
    public void The_windows_shown_follow_the_gate_rows()
    {
        var r = new RaidSettings();
        Assert.False(r.CountShown() || r.DebuffShown());
        r.Gate("act3-g3").CountWindow = true;
        Assert.True(r.CountShown());
        Assert.False(r.DebuffShown());
        r.Gate("act3-g3").DebuffWindow = true;
        Assert.True(r.DebuffShown());
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
    public void The_old_debuff_raid_mirrors_the_table_for_older_versions()
    {
        var s = new Settings();
        s.Raids.Gate(Mordum.Id).DebuffWindow = true;
        s.Raids.SyncDebuffRaid(s.Debuffs);
        Assert.Equal(Raid.모르둠, s.Debuffs.Raid);
        s.Raids.Gate(Mordum.Id).DebuffWindow = false;
        s.Raids.SyncDebuffRaid(s.Debuffs);
        Assert.Equal(Raid.없음, s.Debuffs.Raid);
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
