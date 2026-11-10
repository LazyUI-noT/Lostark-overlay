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
    public void Changed_moves_on_save_and_delete()
    {
        long empty = Store.Changed;
        var file = Store.Save(R(0));
        long saved = Store.Changed;
        Store.Delete(file);
        Assert.NotEqual(empty, saved);
        Assert.NotEqual(saved, Store.Changed);
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
    public void Past_fifty_the_oldest_automatic_record_goes_and_kept_ones_stay()
    {
        Store.Save(R(-10, kept: true));
        for (int i = 0; i < 51; i++) Store.Save(R(i));
        var list = Store.List();
        Assert.Equal(51, list.Count);   // 50 automatic + 1 보관
        Assert.Equal(R(50).StartedAt, list[0].Record.StartedAt);   // newest first
        Assert.DoesNotContain(list, x => x.Record.StartedAt == R(0).StartedAt);
        Assert.Contains(list, x => x.Record.Kept);
    }

    [Fact]
    public void A_kept_record_among_the_newest_takes_one_of_the_places()
    {
        Store.Save(R(0), 3);
        var kept = Store.Save(R(1), 3);
        Store.SetKept(kept, true);
        Store.Save(R(2), 3);
        Store.Save(R(3), 3);   // the newest three: R3, R2 and the kept R1, so R0 goes
        var list = Store.List();
        Assert.DoesNotContain(list, x => x.Record.StartedAt == R(0).StartedAt);
        Assert.Equal(3, list.Count);
        Store.Save(R(4), 3);   // R1 falls out of the newest three: it stays, only under 보관
        Assert.Contains(Store.List(), x => x.File == kept && x.Record.Kept);
        Store.SetKept(kept, false, 3);   // 보관 off out there: to the bin at once
        Assert.DoesNotContain(Store.List(), x => x.File == kept);
        Assert.Contains(Store.Bin.List(), x => x.Record.StartedAt == R(1).StartedAt && !x.Record.Kept);
    }

    // Up to 0.19, N counted only unkept fights and 보관 ones were extra. Now 보관 ones take places, so the first save after the
    // update would bin the unkept fights past N: once, those turn 보관 instead (user, 2026-11-10: the update bins nothing).
    [Fact]
    public void After_the_update_the_fights_past_the_new_count_turn_kept_and_the_next_save_bins_nothing()
    {
        var store = new RecordStore(Path.Combine(_dir, "a"));   // as 0.19 left 가 at keep 3: three unkept, two newer kept
        foreach (var (minute, kept) in new[] { (0, false), (1, false), (2, false), (3, true), (4, true) })
            store.Save(R(minute, kept) with { Character = "가" }, 50);
        store.Save(R(10) with { Character = "나" }, 50);

        Assert.Equal(2, store.KeepOverflow(3));   // newest three: R4, R3 (kept), R2; R1 and R0 past them
        Assert.Equal(0, store.KeepOverflow(3));   // once is enough
        Assert.Empty(store.Bin.List());
        Assert.Contains(store.List(), x => x.Record.StartedAt == R(0).StartedAt && x.Record.Kept);
        store.Save(R(5) with { Character = "가" }, 3);   // the save after: one fight, the ! one (R2), as every save
        Assert.Equal(R(2).StartedAt, Assert.Single(store.Bin.List()).Record.StartedAt);
    }

    [Fact]
    public void Only_the_characters_own_save_pushes_its_fights_to_the_bin()
    {
        Store.Save(R(0) with { Character = "가" }, 1);
        Store.Save(R(1) with { Character = "나" }, 1);   // another character's fight touches nothing of 가's
        Assert.Empty(Store.Bin.List());
        Store.Save(R(2) with { Character = "가" }, 1);
        Assert.Equal(R(0).StartedAt, Assert.Single(Store.Bin.List()).Record.StartedAt);
    }

    [Fact]
    public void Deleting_moves_to_the_bin_and_deleting_there_is_for_good()
    {
        Store.Delete(Store.Save(R(0)));
        Assert.Empty(Store.List());
        var (binned, _) = Assert.Single(Store.Bin.List());
        Store.Bin.Delete(binned);
        Assert.Empty(Store.Bin.List());
        Assert.False(Directory.Exists(Path.Combine(_dir, RecordStore.BinDir, RecordStore.BinDir)));   // no bin in the bin
    }

    [Fact]
    public void Erase_skips_the_bin()
    {
        Store.Erase(Store.Save(R(0)));
        Assert.Empty(Store.List());
        Assert.Empty(Store.Bin.List());
    }

    [Fact]
    public void Restoring_returns_the_fight_kept_with_its_own_time()
    {
        Store.Delete(Store.Save(R(0)));
        Store.Delete(Store.Save(R(0)));   // the same name twice in the bin
        Assert.Equal(2, Store.Bin.List().Count);
        var back = Store.Restore(Store.Bin.List()[0].File, 3);
        var (file, r) = Assert.Single(Store.List());
        Assert.Equal(back, file);
        Assert.True(r.Kept);
        Assert.Equal(R(0).StartedAt, r.StartedAt);
        Assert.Single(Store.Bin.List());
    }

    [Fact]
    public void Restoring_leaves_no_bin_copy_when_the_prune_after_it_fails()
    {
        if (!OperatingSystem.IsWindows()) return;   // relies on Windows' share lock to make the move fail
        Store.Delete(Store.Save(R(1)));
        string older = Store.Save(R(0));   // past N=1 once R(1) is back, so the prune moves it
        using (File.Open(Path.Combine(_dir, older), FileMode.Open, FileAccess.Read, FileShare.Read))   // listed, but not movable
            Assert.ThrowsAny<IOException>(() => Store.Restore(Store.Bin.List()[0].File, 1));
        Assert.Empty(Store.Bin.List());
        Assert.Equal(2, Store.List().Count);
    }

    [Fact]
    public void Emptying_erases_what_entered_before_the_cutoff()
    {
        Store.Delete(Store.Save(R(0)));
        Assert.Equal(0, Store.EmptyBin(DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(1, Store.EmptyBin(DateTime.UtcNow.AddMinutes(1)));
        Assert.Empty(Store.Bin.List());
        Assert.Equal(0, new RecordStore(Path.Combine(_dir, "none")).EmptyBin(DateTime.UtcNow));   // no folder yet
    }

    [Fact]
    public void Lowering_the_number_moves_the_extra_fights_of_every_character()
    {
        foreach (var c in new[] { "가", "나" })
            for (int i = 0; i < 3; i++) Store.Save(R(i) with { Character = c }, 3);
        Assert.Equal(4, Store.OverflowCount(1));
        Assert.Equal(0, Store.OverflowCount(3));
        Store.PruneAll(1);
        Assert.Equal(2, Store.List().Count);
        Assert.Equal(4, Store.Bin.List().Count);
    }

    [Fact]
    public void Unkeeping_says_whether_the_fight_would_go_to_the_bin()
    {
        var old = Store.Save(R(0, kept: true), 2);
        var mid = Store.Save(R(1, kept: true), 2);
        Store.Save(R(2), 2);
        Assert.True(Store.UnkeepWouldBin(old, 2));   // third of two
        Assert.False(Store.UnkeepWouldBin(mid, 2));   // second of two
        Assert.False(Store.UnkeepWouldBin("gone.json.gz", 2));
    }

    [Fact]
    public void An_edit_says_whether_it_would_push_a_fight_to_the_bin()
    {
        foreach (var i in new[] { 10, 11, 12 }) Store.Save(R(i) with { Character = "가" }, 3);
        var other = Store.Save(R(1) with { Character = "나" }, 3);
        Assert.True(Store.WouldBin(other, R(1) with { Character = "가" }, 3));   // older than 가's three
        Assert.False(Store.WouldBin(other, R(20) with { Character = "가" }, 4));   // room left
        Assert.False(Store.WouldBin(other, R(1) with { Character = "나", Gate = "x" }, 3));   // stays 나's only fight
    }

    static readonly string[] Tabs = ["last", "e", "d-kept", "c", "b-kept", "a"];   // "a": 보관 just turned off, past the newest three
    static bool Held(string f) => f == "last";
    static bool IsKept(string f) => f.EndsWith("-kept");

    [Fact]
    public void Recent_is_the_held_fight_and_the_newest_saved_ones_kept_or_not()
    {
        Assert.Equal(["last", "e", "d-kept", "c"], RecordStore.Recent(Tabs, Held, 3));
        Assert.Equal(["e"], RecordStore.Recent(Tabs.Skip(1).ToList(), Held, 0));   // at least one
    }

    [Fact]
    public void Marked_are_the_fights_the_next_save_deletes()
    {
        Assert.Equal(["c", "a"], RecordStore.GoesNext(Tabs, Held, IsKept, 3));   // c: third of three, the next save pushes it out
        Assert.Equal(["a"], RecordStore.GoesNext(Tabs, Held, IsKept, 4));
        Assert.Empty(RecordStore.GoesNext(["b", "a"], Held, IsKept, 3));   // room left
    }

    [Fact]
    public void An_edited_fight_moved_past_the_newest_goes_to_the_bin()
    {
        foreach (var i in new[] { 10, 11, 12 }) Store.Save(R(i) with { Character = "가" }, 3);
        Store.Edit(Store.Save(R(1) with { Character = "나" }, 3), R(1) with { Character = "가" }, 3);
        Assert.DoesNotContain(Store.List(), x => x.Record.StartedAt == R(1).StartedAt);
        Assert.Contains(Store.Bin.List(), x => x.Record.StartedAt == R(1).StartedAt && x.Record.Character == "가");
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
    public void A_saved_fight_takes_its_result_from_the_next_record()
    {
        var file = Store.Save(R(0) with { Cleared = true });
        Store.SetCleared(file, false);   // the same boss came back: a retry
        Assert.Contains(Store.List(), x => x.File == file && x.Record.Cleared == false);
        Store.SetCleared("gone.json.gz", true);   // deleted meanwhile: nothing happens
        Assert.Throws<ArgumentException>(() => Store.SetCleared("C:/x.json.gz", true));
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

    static FightRecord As(FightRecord r, string? character) => r with { Character = character };

    [Fact]
    public void Saving_prunes_only_the_same_characters_automatic_records()
    {
        var store = Store;
        store.Hold(As(R(0), "살살"));
        store.Save(As(R(1, kept: true), "살살"), keep: 2);
        store.Save(As(R(2), "바드짱"), keep: 2);
        store.Save(As(R(3), "살살"), keep: 2);
        store.Save(As(R(4), "살살"), keep: 2);
        store.Save(As(R(5), "살살"), keep: 2);   // the third 살살 auto: the oldest (minute 3) goes

        var left = store.List().Select(x => x.Record.StartedAt.Minute).Order().ToList();

        Assert.Equal([0, 1, 2, 4, 5], left);   // held, 보관, 바드짱's and the two newest 살살 autos
    }

    [Fact]
    public void A_record_of_an_unknown_character_is_pruned_only_among_unknown_ones()
    {
        var store = Store;
        store.Save(As(R(0), "살살"), keep: 1);
        store.Save(As(R(1), null), keep: 1);
        store.Save(As(R(2), null), keep: 1);

        Assert.Equal([0, 2], store.List().Select(x => x.Record.StartedAt.Minute).Order());
    }

    [Fact]
    public void Listing_twice_reads_an_unchanged_file_once()
    {
        var store = Store;
        var file = store.Save(R(0));
        var first = Assert.Single(store.List()).Record;

        Assert.Same(first, Assert.Single(store.List()).Record);
        store.SetKept(file, true);
        Assert.NotSame(first, Assert.Single(store.List()).Record);   // rewritten: read again
    }

    [Fact]
    public void An_edit_rewrites_the_record_under_its_new_name()
    {
        var file = Store.Save(R(0) with { Gate = null, MyClassName = "워로드" });
        var now = Store.Edit(file, R(0) with { Gate = "3막 · 3관문", Difficulty = "하드", Cleared = true, MyClassName = "워로드" });
        Assert.Equal("20261030-200000_3막 · 3관문_하드_워로드 고독한기사.json.gz", now);
        var (name, back) = Assert.Single(Store.List());
        Assert.Equal(now, name);
        Assert.Equal(("3막 · 3관문", "하드", (bool?)true), (back.Gate, back.Difficulty, back.Cleared));
    }

    [Fact]
    public void A_record_moved_to_a_full_character_as_its_oldest_goes_to_the_bin()
    {
        var store = Store;
        string moved = store.Save(R(0) with { Character = "바드짱", MyClassName = "바드", MySubclass = "절실한구원" }, keep: 2);
        string oldest = store.Save(R(1) with { Character = "살살", MyClassName = "워로드" }, keep: 2);
        string kept = store.Save(R(2, kept: true) with { Character = "살살", MyClassName = "워로드" }, keep: 2);
        string newer = store.Save(R(3) with { Character = "살살", MyClassName = "워로드" }, keep: 2);

        var now = store.Edit(moved, R(0) with { Character = "살살", MyClassName = "워로드" }, keep: 2);

        var files = store.List().Select(x => x.File).ToList();
        Assert.DoesNotContain(now, files);   // the group's oldest past 2 (warned before, 휴지통 2026-11-10): to the bin
        Assert.Contains(kept, files);
        Assert.Contains(newer, files);
        Assert.DoesNotContain(oldest, files);   // went at newer's save: third of two
        Assert.DoesNotContain(moved, files);
        Assert.Equal(2, store.Bin.List().Count);
    }

    [Fact]
    public void Naming_a_fight_keeps_a_result_settled_since_it_was_listed()
    {
        var store = Store;
        var file = store.Save(R(0));
        var listed = Assert.Single(store.List()).Record;
        store.SetCleared(file, true);   // settled after the listing (the verdict's task)

        var named = store.SetCharacter(file, "살살");

        Assert.Equal(("살살", (bool?)true), (named!.Character, named.Cleared));
        Assert.Null(listed.Cleared);
        Assert.Equal(("살살", (bool?)true), (Assert.Single(store.List()).Record.Character, Assert.Single(store.List()).Record.Cleared));
    }

    [Fact]
    public void The_held_fight_stays_held_when_edited()
    {
        Store.Hold(R(0));
        Assert.Equal(RecordStore.Held, Store.Edit(RecordStore.Held, R(0) with { Gate = "카제로스" }));
        Assert.Equal("카제로스", Assert.Single(Store.List()).Record.Gate);
    }
}
