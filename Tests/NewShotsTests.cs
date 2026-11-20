using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>preview-images.plan.md §4: 새 캡처, the latest 20 instant shots (files plus a small index, kept across restarts).</summary>
public class NewShotsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"newshots-{Guid.NewGuid():N}");
    static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    static byte[] Png(byte tag) => [137, 80, 78, 71, tag];

    [Fact]
    public void Keeps_only_the_latest_twenty_and_deletes_the_older_files()
    {
        var shots = new NewShots(_dir);
        for (byte i = 0; i < 25; i++) shots.Add(Png(i), Monitor);

        var kept = shots.Entries;

        Assert.Equal(NewShots.Cap, kept.Count);
        Assert.Equal(NewShots.Cap, Directory.GetFiles(_dir, "*.png").Length);
        Assert.Equal((byte)24, File.ReadAllBytes(Path.Combine(_dir, kept[0].File))[4]);   // newest first
        Assert.Equal((byte)5, File.ReadAllBytes(Path.Combine(_dir, kept[^1].File))[4]);
    }

    [Fact]
    public void A_restart_keeps_the_shots_their_areas_and_what_was_seen()
    {
        var first = new NewShots(_dir);
        first.Add(Png(1), Monitor);
        first.Add(Png(2), null);
        first.MarkSeen();
        first.Add(Png(3), new PixelRect(0, 0, 2560, 1440));

        var again = new NewShots(_dir);

        Assert.Equal(first.Entries, again.Entries);
        Assert.Equal(1, again.Unseen);
        Assert.Equal(new PixelRect(0, 0, 2560, 1440), again.Entries[0].Area);
        Assert.Null(again.Entries[1].Area);
    }

    [Fact]
    public void Unseen_counts_new_shots_until_the_list_is_opened()
    {
        var shots = new NewShots(_dir);
        shots.Add(Png(1), Monitor);
        shots.Add(Png(2), Monitor);
        Assert.Equal(2, shots.Unseen);

        shots.MarkSeen();

        Assert.Equal(0, shots.Unseen);
        Assert.Equal(2, shots.Entries.Count);
    }

    [Fact]
    public void Discarding_all_empties_the_list_and_the_folder()
    {
        var shots = new NewShots(_dir);
        shots.Add(Png(1), Monitor);
        shots.Add(Png(2), Monitor);

        shots.DiscardAll();

        Assert.Empty(shots.Entries);
        Assert.Empty(Directory.GetFiles(_dir, "*.png"));
        Assert.Empty(new NewShots(_dir).Entries);
    }

    [Fact]
    public void Replacing_a_slot_puts_the_file_and_its_area_there_and_takes_the_shot_off_the_list()
    {
        var slots = Path.Combine(_dir, "slots");
        Directory.CreateDirectory(slots);
        File.WriteAllBytes(ScreenShots.PathOf(slots, 2), Png(99));
        var s = new Settings();
        s.SlotAreas[2] = null;
        var shots = new NewShots(Path.Combine(_dir, "new"));
        shots.Add(Png(7), new PixelRect(0, 0, 2560, 1440));

        Assert.True(shots.PutInSlot(shots.Entries[0], 2, slots, s));

        Assert.Equal((byte)7, File.ReadAllBytes(ScreenShots.PathOf(slots, 2))[4]);
        Assert.Equal(new PixelRect(0, 0, 2560, 1440), s.SlotAreas[2]);
        Assert.Empty(shots.Entries);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "new"), "*.png"));
    }

    [Fact]
    public void A_shot_whose_file_is_gone_is_dropped_and_not_put_in_a_slot()
    {
        var shots = new NewShots(_dir);
        shots.Add(Png(1), Monitor);
        var entry = shots.Entries[0];
        File.Delete(Path.Combine(_dir, entry.File));
        var s = new Settings();

        Assert.False(shots.PutInSlot(entry, 0, Path.Combine(_dir, "slots"), s));
        Assert.Null(s.SlotAreas[0]);
        Assert.Empty(new NewShots(_dir).Entries);
    }

    [Fact]
    public void A_broken_index_is_ignored_and_the_pictures_on_disk_are_still_listed()
    {
        var shots = new NewShots(_dir);
        shots.Add(Png(1), Monitor);
        File.WriteAllText(Path.Combine(_dir, "index.json"), "{ not json");

        var again = new NewShots(_dir);

        Assert.Single(again.Entries);
        Assert.Null(again.Entries[0].Area);   // the area was in the lost index: a backdrop
    }

    [Fact]
    public void The_capture_key_is_in_All_and_unset_by_default()
    {
        var h = new HotkeySettings();
        Assert.Contains((nameof(HotkeySettings.CaptureNow), ""), h.All());
        Assert.Contains(nameof(HotkeySettings.CaptureNow), new HotkeySettings { CaptureNow = "Ctrl+Shift+D" }.Clashes([]));
    }

    [Fact]
    public void Two_adds_at_once_both_land()
    {
        var shots = new NewShots(_dir);
        Parallel.For(0, 8, i => shots.Add(Png((byte)i), Monitor));
        Assert.Equal(8, shots.Entries.Count);
        Assert.Equal(8, new NewShots(_dir).Entries.Count);
    }
}
