using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>preview-images.plan.md §2: 5 image slots shared by all profiles; a profile holds only which one it shows.</summary>
public class ImageSlotTests
{
    static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    static Settings RoundTrip(Settings s)
    {
        var path = Path.Combine(Path.GetTempPath(), $"slots-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(s);
            return store.Load(out _);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void An_old_file_moves_each_profiles_area_to_its_own_slot()
    {
        var old = new Settings { SlotsMigrated = false };
        old.Profiles[0].Shot = Monitor;
        old.Profiles[2].Shot = new PixelRect(0, 0, 2560, 1440);
        old.Profiles[3].Shot = new PixelRect(0, 0, -1, 10);   // broken: dropped, not carried

        var s = RoundTrip(old);

        Assert.Equal([Monitor, null, new PixelRect(0, 0, 2560, 1440), null, null], s.SlotAreas);
        Assert.Equal([0, 1, 2, 3, 4], s.Profiles.Select(p => p.ShotSlot));
        Assert.All(s.Profiles, p => Assert.Null(p.Shot));
        Assert.True(s.SlotsMigrated);
    }

    [Fact]
    public void The_upgrade_runs_once()
    {
        var s = RoundTrip(new Settings { SlotsMigrated = false });
        s.Profiles[1].ShotSlot = 3;
        s.SlotAreas[3] = Monitor;

        var again = RoundTrip(s);

        Assert.Equal(3, again.Profiles[1].ShotSlot);
        Assert.Equal(Monitor, again.SlotAreas[3]);
    }

    [Fact]
    public void A_new_install_starts_on_slot_N_for_profile_N_with_no_areas()
    {
        var s = new Settings();
        Assert.Equal([0, 1, 2, 3, 4], s.Profiles.Select(p => p.ShotSlot));
        Assert.All(s.SlotAreas, a => Assert.Null(a));
    }

    [Fact]
    public void Changing_a_slots_area_changes_every_profile_on_it()
    {
        var s = new Settings();
        s.Profiles[1].ShotSlot = 0;
        s.SlotAreas[0] = Monitor;

        s.ActiveProfile = 0;
        Assert.Equal(Monitor, s.ShotArea);
        s.ActiveProfile = 1;
        Assert.Equal(Monitor, s.ShotArea);
        Assert.Equal([0, 1], s.ProfilesOnSlot(0));
    }

    [Fact]
    public void An_empty_slot_has_no_area()
    {
        var s = new Settings();
        s.Profiles[0].ShotSlot = 4;
        Assert.Null(s.ShotArea);
    }

    [Fact]
    public void Copying_a_profile_copies_its_slot_pick_and_leaves_the_areas()
    {
        var s = new Settings();
        s.SlotAreas[2] = Monitor;
        s.Profiles[0].ShotSlot = 2;

        Assert.True(s.CopyProfile(3));

        Assert.Equal(2, s.Profiles[3].ShotSlot);
        Assert.Equal(Monitor, s.SlotAreas[2]);
        Assert.Null(s.SlotAreas[3]);
    }

    [Fact]
    public void Undo_keeps_the_areas_as_they_are_now_and_restores_the_pick()
    {
        var before = new Settings();
        before.Profiles[0].ShotSlot = 1;
        var snapshot = SettingsStore.Copy(before);
        var live = new Settings();
        live.Profiles[0].ShotSlot = 4;
        live.SlotAreas[4] = Monitor;   // a file taken after the snapshot: no undo step

        live.CopyFrom(snapshot);

        Assert.Equal(1, live.Profiles[0].ShotSlot);
        Assert.Equal(Monitor, live.SlotAreas[4]);
    }

    [Fact]
    public void A_broken_area_or_pick_is_repaired_on_load()
    {
        var s = new Settings();
        s.SlotAreas[1] = new PixelRect(0, 0, 0, 10);
        s.SlotAreas[2] = Monitor;
        s.Profiles[0].ShotSlot = 9;
        s.Profiles[1].ShotSlot = -2;

        var back = RoundTrip(s);

        Assert.Null(back.SlotAreas[1]);
        Assert.Equal(Monitor, back.SlotAreas[2]);
        Assert.Equal((4, 0), (back.Profiles[0].ShotSlot, back.Profiles[1].ShotSlot));
    }

    [Fact]
    public void A_file_with_a_short_area_list_is_padded()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{ "SlotsMigrated": true, "SlotAreas": [ null ] }""", SettingsStore.Json)!;
        Assert.Equal(OverlayProfile.Count, SettingsStore.Normalize(s).SlotAreas.Length);
    }

    [Theory]
    [InlineData(@"C:\pics\a.png", null)]
    [InlineData("#112233", "#112233")]
    [InlineData(null, null)]
    public void A_picture_file_as_배경화면_goes_back_to_the_game_screen(string? saved, string? expected)
    {
        var s = new Settings { PreviewBack = saved };
        Assert.Equal(expected, RoundTrip(s).PreviewBack);
    }
}
