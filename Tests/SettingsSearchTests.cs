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

    [Theory]
    [InlineData("감지 방식", "읽는 방식")]
    [InlineData("공유 방", "파티 공유 참가")]
    [InlineData("편집 중", "편집할 직업각인")]
    public void An_old_word_still_finds_the_row_under_its_new_word(string typed, string label)
    {
        var rows = new[] { new SettingEntry("a", label, "x"), new SettingEntry("b", "다른 것", "x") };

        Assert.Equal(["a"], SettingsSearch.Find(rows, typed).Select(e => e.Id));
    }

    [Theory]   // reading-place.plan.md A: the fit section and 표시할 화면 became 화면·인식 → 인게임 설정
    [InlineData("스킬창 맞추기")]
    [InlineData("표시할 화면")]
    public void An_old_section_name_finds_the_rows_now_under_in_game_settings(string typed)
    {
        var rows = new[] { new SettingEntry("a", "표시 화면", "화면·인식 › 인게임 설정"), new SettingEntry("b", "다른 것", "x") };

        Assert.Equal(["a"], SettingsSearch.Find(rows, typed).Select(e => e.Id));
    }

    [Fact]
    public void The_old_boss_debuff_size_name_finds_the_target_ui_row_and_the_shown_rows_are_found()
    {
        SettingEntry[] all = [new("size", "타겟 UI 버프 크기 (보스) (%)", "인게임 설정"), new("n1", "버프 표시", "인게임 설정"), new("n2", "타겟 UI 버프 표시", "인게임 설정")];
        Assert.Equal(["size"], SettingsSearch.Find(all, "보스 디버프 크기").Select(e => e.Id));
        Assert.Equal(["n1", "n2"], SettingsSearch.Find(all, "버프 표시").Select(e => e.Id));
    }
}
