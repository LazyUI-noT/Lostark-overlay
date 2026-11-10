using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The setting rows the rail's orbs count (<see cref="RailNews.RowKeys"/>): a repeated row's " #n" suffix is dropped, and
/// a row of a section that depends on the picked class, character, party member or canvas box is no news (it changes with the picking).</summary>
public class RailRowKeysTests
{
    [Fact]
    public void A_repeated_rows_suffix_is_dropped() =>
        Assert.Equal("내 오버레이/전체/색", RailNews.RowKey("내 오버레이/전체/색#2"));

    [Fact]
    public void A_hash_that_is_not_a_suffix_stays() =>
        Assert.Equal("화면·인식/감지/#이름", RailNews.RowKey("화면·인식/감지/#이름"));

    [Fact]
    public void Rows_of_a_picked_class_member_or_box_are_no_news() =>
        Assert.Equal(["내 오버레이/전체/y", "직업 인식/직업 인식/w"],
            RailNews.RowKeys(["내 오버레이/스킬 칸/x", "내 오버레이/전체/y", "파티/파티원 모양/z", "직업 인식/직업 인식/w"]));

    [Fact]
    public void Repeated_rows_count_once() =>
        Assert.Equal(["a/b/c"], RailNews.RowKeys(["a/b/c", "a/b/c#2", "a/b/c#3"]));
}
