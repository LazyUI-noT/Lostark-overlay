using System.IO.Compression;
using System.Text;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>전투 기록 내보내기 / 가져오기 (homework.plan.md): fights shared as a file, names left out.</summary>
public class RecordExportTests
{
    static FightRecord Fight(int minute = 0) =>
        new(new DateTime(2026, 11, 4, 20, minute, 0), "고통의 마녀 · 2관문", 300, [120.5], "살살파티명", 3, "고독한기사",
            [new RecordMember("살살파티명", 3, MeterRole.딜러, 1), new RecordMember("친구A", 5, null, 1), new RecordMember("친구B", 7, null, 2)],
            [new RecordSample(1, "살살파티명", 100, 10), new RecordSample(1, "친구A", 50, 5), new RecordSample(2, "친구B", 70, 7)],
            [new RecordBoss(1, 400, 55, 100)],
            [new RecordEvent(1.3, "살살파티명", RecordEventKind.사용, (int)SlotId.일반3), new RecordEvent(2, "친구A", RecordEventKind.버프시작, 1)],
            Kept: true, MyClassName: "워로드", Difficulty: "하드", Cleared: true, Character: "너무아파요살살");

    [Fact]
    public void An_exported_fight_carries_no_names()
    {
        var r = RecordExport.Anonymous(Fight());

        Assert.Equal("나", r.Me);
        Assert.Equal(["나", "파티원 1", "파티원 2"], r.Members.Select(m => m.Name));
        Assert.Equal(["나", "파티원 1", "파티원 2"], r.Samples.Select(s => s.Name));
        Assert.Equal(["나", "파티원 1"], r.Events.Select(e => e.Name));
        Assert.Null(r.Character);
        Assert.False(r.Kept);
        Assert.Equal(("워로드", "고독한기사", "하드", (bool?)true), (r.MyClassName, r.MySubclass, r.Difficulty, r.Cleared));   // the rest as it was
        Assert.Equal(Fight().MyShare, r.MyShare);
    }

    [Fact]
    public void Fights_written_are_read_back()
    {
        using var file = new MemoryStream();
        RecordExport.Write(file, [Fight(0), Fight(5)]);
        file.Position = 0;

        var back = RecordExport.Read(file);

        Assert.Equal(2, back.Count);
        Assert.Equal(["나", "파티원 1", "파티원 2"], back[0].Members.Select(m => m.Name));
        Assert.Equal(new DateTime(2026, 11, 4, 20, 5, 0), back[1].StartedAt);
    }

    [Theory]
    [InlineData(RecordEventKind.버프시작, 999)]   // no such buff: the timeline's lookup would throw
    [InlineData(RecordEventKind.암수시작, 77)]
    [InlineData(RecordEventKind.사용, -5)]
    public void A_fight_with_an_event_of_nothing_known_is_not_drawable(RecordEventKind kind, int what)
    {
        Assert.False(RecordStore.IsValid(Fight() with { Events = [new RecordEvent(1, "살살파티명", kind, what)] }));
    }

    [Fact]
    public void A_fight_with_a_time_that_is_not_a_number_is_not_drawable()
    {
        Assert.False(RecordStore.IsValid(Fight() with { Samples = [new RecordSample(double.NaN, "살살파티명", 1, 1)] }));
        Assert.False(RecordStore.IsValid(Fight() with { Boss = [new RecordBoss(double.PositiveInfinity, 1, 1, 1)] }));
        Assert.True(RecordStore.IsValid(Fight()));
    }

    [Fact]
    public void An_imported_fight_too_big_to_draw_is_skipped()
    {
        Assert.True(RecordExport.Fits(Fight()));
        Assert.False(RecordExport.Fits(Fight() with { Gate = new string('가', 500) }));
        Assert.False(RecordExport.Fits(Fight() with { Members = [.. Enumerable.Range(0, 40).Select(i => new RecordMember($"m{i}", 1, null, 1))] }));
        Assert.False(RecordExport.Fits(Fight() with { Events = [.. Enumerable.Repeat(Fight().Events[0], RecordExport.MaxEvents + 1)] }));
    }

    [Fact]
    public void A_file_that_is_not_an_export_reads_as_no_fights()
    {
        Assert.Empty(RecordExport.Read(new MemoryStream(Encoding.UTF8.GetBytes("not gzip"))));

        using var gz = new MemoryStream();
        using (var w = new GZipStream(gz, CompressionLevel.Fastest, leaveOpen: true)) w.Write(Encoding.UTF8.GetBytes("{\"fights\": [ {\"me\": null} ]}"));
        gz.Position = 0;
        Assert.Empty(RecordExport.Read(gz));   // a fight missing its parts is skipped
    }
}
