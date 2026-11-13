using System.Text.Json;
using System.Text.Json.Nodes;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>줄 수 창 켜기, 빛날 때만 표시 and the glow rules per profile (class-profiles.plan.md E; one per profile, not per class, since 2026-11-14).</summary>
public class RaidShowTests
{
    const string Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세";

    static Settings Playing(string? key)
    {
        var s = new Settings();
        s.Characters[Lone] = new() { Class = "워로드", Subclass = "고독한기사" };
        s.Characters[Combat] = new() { Class = "워로드", Subclass = "전투태세" };
        Go(s, key);
        return s;
    }

    static void Go(Settings s, string? key)
    {
        s.ActiveCharacter = key;
        s.SyncClass();
    }

    static Settings LoadWithout(Settings s)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(s, SettingsStore.Json))!;
        foreach (var p in json["Profiles"]!.AsArray())
        {
            p!.AsObject().Remove("RaidShow");
            foreach (var (_, e) in p["Classes"]!.AsObject()) e!.AsObject().Remove("RaidShow");
        }
        return SettingsStore.Normalize(json.Deserialize<Settings>(SettingsStore.Json)!);
    }

    [Fact]
    public void Old_file_fills_from_live()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        Go(s, Combat);   // 고독한기사 has an entry of its own
        (s.Raids.CountEnabled, s.Raids.OnlyWhenGlowing, s.Raids.GlowUntil, s.Raids.GlowBars) = (false, true, GlowUntil.Bars, 7);

        var back = LoadWithout(s);

        var want = new RaidShow(false, true, true, GlowUntil.Bars, 20, 7);
        Assert.All(back.Profiles, p => Assert.Equal(want, p.RaidShow));
        Assert.Equal(want, back.Profiles[0].Classes![Lone].RaidShow);
    }

    [Fact]
    public void Missing_record_never_turns_count_off()
    {
        var s = Playing(null);
        var entry = new ProfileEntry();
        entry.CaptureFrom(s);
        entry.RaidShow = null;
        s.Raids.CountEnabled = true;

        entry.ApplyTo(s);

        Assert.True(s.Raids.CountEnabled);
        Assert.Null(JsonSerializer.Deserialize<ProfileEntry>("{}", SettingsStore.Json)!.RaidShow);   // never a record of defaults
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "OnlyWhenGlowing": true }""")]
    public void Partial_record_never_turns_count_off(string json)
    {
        var show = JsonSerializer.Deserialize<RaidShow>(json, SettingsStore.Json)!;

        var fresh = new RaidSettings();
        Assert.Equal((fresh.CountEnabled, fresh.Glow, fresh.GlowUntil, fresh.GlowSeconds, fresh.GlowBars),
            (show.CountEnabled, show.Glow, show.GlowUntil, show.GlowSeconds, show.GlowBars));
    }

    [Fact]
    public void Class_switch_keeps_count_read()   // one per profile since 2026-11-14 (RaidPerProfileTests)
    {
        var s = Playing(Lone);
        (s.Raids.CountEnabled, s.Raids.GlowSeconds) = (false, 45);

        Go(s, Combat);
        Assert.Equal((false, 45), (s.Raids.CountEnabled, s.Raids.GlowSeconds));
    }

    [Fact]
    public void Profile_switch_changes_count_read()
    {
        var s = SettingsStore.Normalize(SettingsStore.Copy(Playing(Lone)));   // as loaded: every profile filled
        s.Raids.Glow = false;
        s.SwitchProfile(1);
        Assert.True(s.Raids.Glow);
        s.SwitchProfile(0);
        Assert.False(s.Raids.Glow);
    }

    [Fact]
    public void Hand_edited_numbers_are_clamped()
    {
        var s = Playing(null);
        s.Profiles[1].RaidShow = new RaidShow(true, false, true, GlowUntil.Seconds, -5, 99999);

        var back = SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, SettingsStore.Json), SettingsStore.Json)!);

        Assert.Equal((1, RaidSettings.MaxGlowBars), (back.Profiles[1].RaidShow!.GlowSeconds, back.Profiles[1].RaidShow!.GlowBars));
    }
}
