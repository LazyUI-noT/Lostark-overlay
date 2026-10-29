using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class SettingsSearchTests
{
    static readonly SettingEntry[] All =
    [
        new("slot", "스킬 칸 크기", "스킬 › 모양"),
        new("bar", "버프 막대 두께", "버프 › 모양", Help: "막대가 굵을수록 멀리서 보기 쉽습니다"),
        new("raid", "보스 줄 수 창 크기", "보스 줄 수 › 모양"),
        new("hud", "게임 HUD 크기", "게임 설정 값"),
        new("gamebuff", "게임 버프 크기", "게임 설정 값"),
        new("peer", "파티원 아이콘 크기", "파티 › 모양"),
        new("gap", "끊긴 피증 막대 숨김", "버프 › 표시"),
    ];

    static string[] Ids(string q) => SettingsSearch.Find(All, q).Select(e => e.Id).ToArray();

    [Fact]
    public void Label_matches_come_before_path_matches_and_keep_registration_order()
    {
        Assert.Equal(["slot", "raid", "hud", "gamebuff", "peer"], Ids("크기"));
    }

    [Theory]
    [InlineData("버프크기")]
    [InlineData("버프 크기")]
    [InlineData("버프 크기 ")]
    public void Spaces_are_ignored(string q) => Assert.Equal("gamebuff", Ids(q)[0]);

    [Fact]
    public void A_word_inside_a_label_is_found() => Assert.Equal(["gap"], Ids("피증"));

    [Fact]
    public void Ranks_are_start_then_contains_then_path_then_help()
    {
        // "버프": label start (bar), label contains (gamebuff), path only (gap).
        Assert.Equal(["bar", "gamebuff", "gap"], Ids("버프"));
        // "굵을": help only.
        Assert.Equal(["bar"], Ids("굵을"));
        // "막대": labels (bar, gap) before nothing else; help match on bar is not counted twice.
        Assert.Equal(["bar", "gap"], Ids("막대"));
    }

    [Fact]
    public void A_help_only_match_ranks_last()
    {
        SettingEntry[] all = [new("h", "다른 이름", "기타", Help: "두께"), new("l", "선 두께", "기타")];
        Assert.Equal(["l", "h"], SettingsSearch.Find(all, "두께").Select(e => e.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_query_finds_nothing(string q) => Assert.Empty(Ids(q));

    [Fact]
    public void Case_is_ignored_and_results_are_capped()
    {
        Assert.Equal(["hud"], Ids("hud"));
        Assert.Equal(2, SettingsSearch.Find(All, "크기", max: 2).Count);
    }
}
