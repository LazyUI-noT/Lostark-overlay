using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class SettingsHistoryTests
{
    [Fact]
    public void Undo_gives_the_snapshot_before_and_redo_the_one_after()
    {
        var h = new SettingsHistory();
        h.Record("A");   // about to change A into B
        Assert.True(h.CanUndo);
        Assert.Equal("A", h.Undo("B"));
        Assert.True(h.CanRedo);
        Assert.Equal("B", h.Redo("A"));
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void A_new_change_after_undo_drops_the_redo()
    {
        var h = new SettingsHistory();
        h.Record("A");
        h.Undo("B");
        h.Record("A");   // a different change from A
        Assert.False(h.CanRedo);
        Assert.Null(h.Redo("C"));
    }

    [Fact]
    public void Only_the_newest_steps_are_kept()
    {
        var h = new SettingsHistory(capacity: 2);
        h.Record("A");
        h.Record("B");
        h.Record("C");
        Assert.Equal("C", h.Undo("D"));
        Assert.Equal("B", h.Undo("C"));
        Assert.Null(h.Undo("B"));
    }

    [Fact]
    public void Recording_the_same_snapshot_twice_is_one_step()
    {
        var h = new SettingsHistory();
        h.Record("A");
        h.Record("A");
        Assert.Equal("A", h.Undo("B"));
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Nothing_to_undo_gives_null() => Assert.Null(new SettingsHistory().Undo("A"));

    /// <summary>Snapshots through the settings serializer restore a profile switch and a layout move whole.</summary>
    [Fact]
    public void A_snapshot_restores_the_profile_and_the_layout()
    {
        var s = new Settings();
        string before = JsonSerializer.Serialize(s, SettingsStore.Json);
        var h = new SettingsHistory();
        h.Record(before);
        s.SwitchProfile(2);
        s.Overlay.Left = 123;

        var back = JsonSerializer.Deserialize<Settings>(h.Undo(JsonSerializer.Serialize(s, SettingsStore.Json))!, SettingsStore.Json)!;

        Assert.Equal(before, JsonSerializer.Serialize(back, SettingsStore.Json));
        Assert.Equal(0, back.ActiveProfile);
    }

    [Fact]
    public void CopyFrom_makes_the_live_object_equal_to_the_snapshot()
    {
        var before = new Settings();
        before.SwitchProfile(2);
        before.Overlay.Left = 123;
        before.FavoriteClasses = ["바드"];
        string json = JsonSerializer.Serialize(before, SettingsStore.Json);
        var live = new Settings();

        live.CopyFrom(JsonSerializer.Deserialize<Settings>(json, SettingsStore.Json)!);

        Assert.Equal(json, JsonSerializer.Serialize(live, SettingsStore.Json));
        Assert.Equal(2, live.ActiveProfile);
    }

    [Fact]
    public void CopyFrom_keeps_what_the_app_learned_meanwhile()
    {
        var snapshot = new Settings { ActiveCharacter = "바드 · 진실된 용기", FirstRunDone = false };
        snapshot.Overlay.Left = 10;
        var live = new Settings { ActiveCharacter = "워로드 · 고독한기사", FirstRunDone = true };
        live.Fits["k"] = new Fit(100, 0, 0, default, default, default);
        live.StackSkills["워로드 · 고독한기사"] = ["일반1"];
        live.Party.TrustedDevices.Add(new TrustedDevice("d", "n"));

        live.CopyFrom(snapshot);

        Assert.Equal(10, live.Overlay.Left);   // an edit: undone
        Assert.Equal("워로드 · 고독한기사", live.ActiveCharacter);   // detected or picked meanwhile: kept
        Assert.True(live.FirstRunDone);
        Assert.Single(live.Fits);
        Assert.Single(live.StackSkills);
        Assert.Single(live.Party.TrustedDevices);
    }
}
