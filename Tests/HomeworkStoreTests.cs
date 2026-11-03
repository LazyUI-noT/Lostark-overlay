using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>homework.json: the clears kept apart from 전투 기록 (a deleted fight never un-clears a raid).</summary>
public sealed class HomeworkStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "go-homework-" + Guid.NewGuid().ToString("N"));
    string Path_ => Path.Combine(_dir, "homework.json");
    static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0);   // Thursday: the week from 2026-11-04 06:00

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    static HomeworkClear Clear(int day, int hour = 20, string raid = "serka") => new("살살", raid, "하드", new DateTime(2026, 11, day, hour, 0, 0));

    [Fact]
    public void Added_clears_are_read_back()
    {
        var store = new HomeworkStore(Path_);

        Assert.True(store.Add(Clear(4), Now));
        Assert.Equal([Clear(4)], new HomeworkStore(Path_).Load());
    }

    [Fact]
    public void The_same_clear_twice_is_kept_once()
    {
        var store = new HomeworkStore(Path_);
        store.Add(Clear(4), Now);

        Assert.False(store.Add(Clear(4), Now));
        Assert.Single(store.Load());
    }

    [Fact]
    public void Clears_from_before_this_week_are_dropped_on_the_next_add()
    {
        var store = new HomeworkStore(Path_);
        store.Add(Clear(3), Now);   // Tuesday: last week

        store.Add(Clear(4, raid: "belgardin"), Now);

        Assert.Equal([Clear(4, raid: "belgardin")], store.Load());
    }

    [Fact]
    public void A_clear_from_before_this_week_is_not_kept()
    {
        var store = new HomeworkStore(Path_);

        Assert.False(store.Add(Clear(4, hour: 5), Now));   // Wednesday 05:00: last week's (a fight backfilled late)
        Assert.Empty(store.Load());
    }

    [Fact]
    public void A_clear_can_be_taken_back()
    {
        var store = new HomeworkStore(Path_);
        store.Add(Clear(4), Now);

        Assert.True(store.Remove(Clear(4)));
        Assert.Empty(store.Load());
        Assert.False(store.Remove(Clear(4)));
    }

    [Fact]
    public void A_missing_or_broken_file_reads_as_no_clears()
    {
        Assert.Empty(new HomeworkStore(Path_).Load());
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{ not json");
        Assert.Empty(new HomeworkStore(Path_).Load());
    }

    [Fact]
    public void The_shipped_raids_json_tracks_only_gates_it_knows()
    {
        string profiles = Path.Combine(Helpers.RepoRoot(), "Profiles");
        var gates = RaidData.Load(profiles);
        var raids = HomeworkData.Load(profiles);

        Assert.NotEmpty(raids);
        Assert.Contains(raids, r => r.Name == "세르카" && r.Levels.Select(l => l.Tag).SequenceEqual(["세매", "세노", "세하", "세나메"]));
        foreach (var raid in raids)
        {
            Assert.All(raid.Gates, id => Assert.Contains(gates, g => g.Id == id));
            Assert.All(raid.Levels, l => Assert.All(l.Last, id => Assert.Contains(id, raid.Gates)));
        }
    }
}
