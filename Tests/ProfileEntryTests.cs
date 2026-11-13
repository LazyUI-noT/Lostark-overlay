using System.Reflection;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>One profile · class entry (class-profiles.plan.md, "Stored model").</summary>
public class ProfileEntryTests
{
    [Fact]
    public void Every_property_is_a_place_or_in_a_LookGroup()
    {
        var props = typeof(ProfileEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToList();
        var looks = ProfileEntry.Looks.SelectMany(kv => kv.Value).ToList();

        Assert.Equal(props, [.. ProfileEntry.Places.Concat(looks).Order()]);   // each exactly once
        Assert.All(Enum.GetValues<LookGroup>(), g => Assert.NotEmpty(ProfileEntry.Looks[g]));
    }

    [Fact]
    public void Profile_only_parts_are_not_in_the_entry()
    {
        var names = typeof(ProfileEntry).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(nameof(OverlayProfile.Shot), names);
        Assert.DoesNotContain(nameof(OverlayProfile.SubclassPlaces), names);
        Assert.DoesNotContain(nameof(OverlayProfile.PartySlots), names);
        Assert.DoesNotContain(names, n => n.StartsWith("Party") || n.StartsWith("BoundParty"));
    }

    [Fact]
    public void Taking_a_look_changes_only_its_own_properties()
    {
        foreach (var look in Enum.GetValues<LookGroup>())
        {
            var s = new Settings();
            var entry = new ProfileEntry();
            entry.Fill(s);
            var before = Values(entry);
            Change(s);

            entry.TakeLook(look, s);

            var changed = Values(entry).Where(kv => kv.Value != before[kv.Key]).Select(kv => kv.Key).ToList();
            Assert.All(changed, name => Assert.Contains(name, ProfileEntry.Looks[look]));
        }
    }

    static void Change(Settings s)
    {
        (s.Overlay.Opacity, s.Overlay.GoldColor, s.Gauge.Thickness, s.Gauge.Fill) = (0.3, "#010101", 9, "#020202");
        (s.Amsu.Color, s.Judgment.Opacity, s.Meter.Opacity, s.Meter.MyColor) = ("#030303", 0.5, 0.4, "#040404");
        (s.Buffs.Drain, s.Debuffs.HideWhenEmpty, s.Raids.Background) = (false, true, "#050505");
    }

    static Dictionary<string, string> Values(ProfileEntry e) =>
        typeof(ProfileEntry).GetProperties().ToDictionary(p => p.Name,
            p => System.Text.Json.JsonSerializer.Serialize(p.GetValue(e), GaugeOverlay.Core.Data.SettingsStore.Json));
}
