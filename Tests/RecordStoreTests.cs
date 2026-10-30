using System.IO.Compression;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Saved fights on disk (spec § D Keeping): one gzip JSON file each, the last 20 automatic kept, 보관 kept for good,
/// unreadable files skipped.</summary>
public sealed class RecordStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "go-records-" + Guid.NewGuid().ToString("N"));
    RecordStore Store => new(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    static FightRecord R(int minute, bool kept = false) =>
        new(new DateTime(2026, 10, 30, 20, 0, 0).AddMinutes(minute), "act3-g3", 300, [120.5], "나", 3, "고독한기사",
            [new RecordMember("나", 3, MeterRole.딜러, 1), new RecordMember("하나", 5, null, 1)],
            [new RecordSample(1, "나", 100_000_000, 1_000_000)], [new RecordBoss(1, 400_000_000, 55, 100)],
            [new RecordEvent(1.3, "나", RecordEventKind.사용, (int)SlotId.일반3)], kept);

    [Fact]
    public void A_record_comes_back_as_saved()
    {
        var file = Store.Save(R(0));
        var (name, back) = Assert.Single(Store.List());
        Assert.Equal(file, name);
        Assert.Equal("20261030-200000_act3-g3_고독한기사.json.gz", file);
        Assert.Equal(R(0).Events, back.Events);
        Assert.Equal(R(0).Members, back.Members);
        Assert.Equal(R(0).Phases, back.Phases);
        Assert.Equal((R(0).Gate, R(0).MySubclass, R(0).Duration), (back.Gate, back.MySubclass, back.Duration));
    }

    [Fact]
    public void The_file_name_says_when_where_and_with_what_class()
    {
        Assert.Equal("20261030-200000_3막 · 3관문_워로드 고독한기사.json.gz", Store.Save(R(0) with { Gate = "3막 · 3관문", MyClassName = "워로드" }));
        Assert.Equal("20261030-200100_워로드.json.gz", Store.Save(R(1) with { Gate = null, MySubclass = null, MyClassName = "워로드" }));
        Assert.Equal("20261030-200200_3막 · 3관문_EX 나이트메어_워로드.json.gz", Store.Save(R(2) with { Gate = "3막 · 3관문", Difficulty = "EX 나이트메어", MySubclass = null, MyClassName = "워로드" }));
        Assert.Equal("20261030-200200_a-b.json.gz", Store.Save(R(2) with { Gate = "a/b", MySubclass = null }));   // never a path
        Assert.Equal(4, Store.List().Count);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(0, 1)]     // at least one
    [InlineData(99, 50)]   // at most fifty
    public void Saving_keeps_the_chosen_number_of_automatic_records_and_every_kept_one(int keep, int left)
    {
        Store.Save(R(-10, kept: true));
        for (int i = 0; i < 60; i++) Store.Save(R(i), keep);
        var list = Store.List();
        Assert.Equal(left, list.Count(x => !x.Record.Kept));
        Assert.Equal(R(59).StartedAt, list[0].Record.StartedAt);
        Assert.Single(list, x => x.Record.Kept);
    }

    [Fact]
    public void Two_fights_in_the_same_second_get_their_own_files()
    {
        Assert.NotEqual(Store.Save(R(0)), Store.Save(R(0)));
        Assert.Equal(2, Store.List().Count);
    }

    [Fact]
    public void Past_twenty_the_oldest_automatic_record_goes_and_kept_ones_stay()
    {
        Store.Save(R(-10, kept: true));
        for (int i = 0; i < 21; i++) Store.Save(R(i));
        var list = Store.List();
        Assert.Equal(21, list.Count);   // 20 automatic + 1 보관
        Assert.Equal(R(20).StartedAt, list[0].Record.StartedAt);   // newest first
        Assert.DoesNotContain(list, x => x.Record.StartedAt == R(0).StartedAt);
        Assert.Contains(list, x => x.Record.Kept);
    }

    [Fact]
    public void Kept_survives_the_prune()
    {
        var first = Store.Save(R(0));
        Store.SetKept(first, true);
        for (int i = 1; i <= 21; i++) Store.Save(R(i));
        Assert.Contains(Store.List(), x => x.File == first && x.Record.Kept);
    }

    [Fact]
    public void Unreadable_files_are_skipped()
    {
        Store.Save(R(0));
        File.WriteAllBytes(Path.Combine(_dir, "20261030-210000.json.gz"), []);
        File.WriteAllText(Path.Combine(_dir, "20261030-220000.json.gz"), "not gzip");
        using (var gz = new GZipStream(File.Create(Path.Combine(_dir, "20261030-230000.json.gz")), CompressionLevel.Fastest))
            gz.Write("{\"Gate\": [1,"u8);
        using (var gz = new GZipStream(File.Create(Path.Combine(_dir, "20261030-235900.json.gz")), CompressionLevel.Fastest))
            gz.Write("{\"Me\": null}"u8);
        Assert.Single(Store.List());
    }

    [Fact]
    public void Records_with_values_the_timeline_cannot_draw_are_skipped()
    {
        Store.Save(R(0) with { Duration = 1e15 });
        Store.Save(R(1) with { Phases = [-1] });
        Store.Save(R(2) with { Phases = [double.NegativeInfinity] });
        Store.Save(R(3) with { Members = [new RecordMember(null!, 0, null, 1)] });
        Store.Save(R(4) with { Events = [new RecordEvent(1, null!, RecordEventKind.사용, 0)] });
        Store.Save(R(5) with { Samples = [new RecordSample(1, null!, 0, 0)] });
        Store.Save(R(6));
        Assert.Equal(R(6).StartedAt, Assert.Single(Store.List()).Record.StartedAt);
    }

    [Fact]
    public void Names_with_a_path_are_refused()
    {
        Store.Save(R(0));
        Assert.Throws<ArgumentException>(() => Store.Delete(@"..\settings.json"));
        Assert.Throws<ArgumentException>(() => Store.SetKept("C:/x.json.gz", true));
        Store.Delete(Store.List()[0].File);
        Assert.Empty(Store.List());
    }

    [Fact]
    public void A_half_written_file_left_by_a_crash_is_cleared_on_the_next_save()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "20261030-190000.json.gz.tmp"), "half");
        Store.Save(R(0));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void No_folder_yet_means_no_records() => Assert.Empty(Store.List());

    [Fact]
    public void With_auto_save_off_only_the_last_fight_is_held_and_the_next_replaces_it()
    {
        Store.Hold(R(0));
        Store.Hold(R(1));
        var (file, r) = Assert.Single(Store.List());
        Assert.True(RecordStore.IsHeld(file));
        Assert.Equal(R(1).StartedAt, r.StartedAt);
    }

    [Fact]
    public void The_held_fight_is_listed_first_and_never_pruned()
    {
        Store.Hold(R(5));
        Store.Save(R(0), keep: 1);
        Store.Save(R(1), keep: 1);
        Assert.Equal([true, false], Store.List().Select(x => RecordStore.IsHeld(x.File)));
    }

    [Fact]
    public void Keeping_the_held_fight_saves_it_under_its_own_name()
    {
        Store.Hold(R(0));
        Store.SetKept(RecordStore.Held, true);
        var (file, r) = Assert.Single(Store.List());
        Assert.Equal("20261030-200000_act3-g3_고독한기사.json.gz", file);
        Assert.True(r.Kept);
    }
}
