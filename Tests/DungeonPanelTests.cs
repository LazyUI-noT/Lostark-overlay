using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The top-left dungeon panel as Windows OCR reads it (lines taken from the footage, noise and all): a raid with its
/// gate name and difficulty, or a guardian raid (not recorded).</summary>
public sealed class DungeonPanelTests
{
    [Theory]
    [InlineData("3막 : 질혹, 폭풍의 밤|표싱글 모드]", "질혹, 폭풍의 밤", "싱글")]
    [InlineData("3막 : 질혹, 폭풍의 방|•I심글 모드]|재시작", "질혹, 폭풍의 방", "싱글")]
    [InlineData("|막 : 대지를 부수는 업화의 궤적|{싱글 모드] 7'", "대지를 부수는 업화의 궤적", "싱글")]
    [InlineData(": 대지를 부수는 업화의 훼적|“1심글모드] /|재시작", "대지를 부수는 업화의 훼적", "싱글")]
    [InlineData("종막 : 최후의 '별|[EX 니이트에어lz", "최후의 별", "EX 나이트메어")]   // stray marks dropped
    [InlineData("2막 : 부유하는 악몽의 진혼곡|[하드]", "부유하는 악몽의 진혼곡", "하드")]
    [InlineData("1막 : 대지를 부수는 업화의 궤적|[노말]", "대지를 부수는 업화의 궤적", "노말")]
    [InlineData("3막 : 칠흑, 폭풍의 밤|[나이트메어]", "칠흑, 폭풍의 밤", "나이트메어")]
    [InlineData("죽음의 계율자, 벨가르닌|[노말1|재시작", "죽음의 계율자, 벨가르닌", "노말")]   // no "N막 :": the line above the difficulty
    [InlineData("죽음의 계율자, 벨가르딘|[매칭]|재시작", "죽음의 계율자, 벨가르딘", "매칭")]   // 8인 매칭
    [InlineData("구원의 종탑|[3단계]|재시작", "구원의 종탑", "3단계")]   // 구원의 종탑's 단계 (a friend's 1440p clip)
    [InlineData("구원의 종탑|[1 단계]", "구원의 종탑", "1단계")]
    [InlineData("구원의 종탑|[2단계]|재시작", "구원의 종탑", "2단계")]
    public void A_raid_panel_gives_its_name_and_difficulty(string lines, string title, string difficulty)
    {
        var d = DungeonPanel.Parse(lines.Split('|'));
        Assert.Equal(new DungeonInfo(DungeonKind.Raid, title, difficulty), d);
    }

    [Theory]
    [InlineData("15羽9|목표 가디언|彎囹 드 齒 넣 말 ㉧|이그력시온의 잔영 1단계")]
    [InlineData("목표 가디언")]
    [InlineData("19 : 13|토벌 중단")]   // a countdown has a colon too
    public void A_guardian_raid_is_told_apart(string lines) =>
        Assert.Equal(DungeonKind.Guardian, DungeonPanel.Parse(lines.Split('|'))!.Kind);

    [Theory]
    [InlineData("")]
    [InlineData("1431 囹 드 齒|광폭화까지|00: 1248")]
    public void Anything_else_is_nothing(string lines) => Assert.Null(DungeonPanel.Parse(lines.Split('|')));

    [Fact]
    public void Only_the_restart_button_still_says_raid() =>
        Assert.Equal(new DungeonInfo(DungeonKind.Raid, null, null), DungeonPanel.Parse(["재시작"]));

    static DungeonState Feed(params DungeonInfo[] reads) => reads.Aggregate(DungeonState.None, DungeonPanel.Next);
    static DungeonInfo Raid(string? title, string? difficulty = "싱글") => new(DungeonKind.Raid, title, difficulty);

    [Fact]
    public void A_misread_name_gives_way_to_the_one_read_most()
    {
        var s = Feed(Raid("질혹, 폭풍의 밤"), Raid("칠흑, 폭풍의 밤"), Raid("칠흑, 폭풍의 밤"), Raid("질혹, 폭풍의 밤"), Raid("칠흑, 폭풍의 밤"));
        Assert.Equal("칠흑, 폭풍의 밤", s.Shown!.Title);
    }

    [Fact]
    public void A_read_that_missed_a_line_keeps_the_earlier_one() =>
        Assert.Equal(Raid("칠흑, 폭풍의 밤", "하드"), Feed(Raid("칠흑, 폭풍의 밤", "하드"), Raid(null, null)).Shown);

    [Fact]
    public void Another_dungeon_starts_over()
    {
        var s = Feed(Raid("칠흑, 폭풍의 밤"), Raid("칠흑, 폭풍의 밤"), Raid("칠흑, 폭풍의 밤"), Raid("대지를 부수는 업화의 궤적", "노말"));
        Assert.Equal(Raid("대지를 부수는 업화의 궤적", "노말"), s.Shown);
        Assert.Equal(DungeonKind.Guardian, DungeonPanel.Next(s, new(DungeonKind.Guardian, null, null)).Shown!.Kind);
    }

    [Fact]
    public void A_read_that_lost_the_ex_keeps_it() =>
        Assert.Equal("EX 나이트메어", Feed(Raid("최후의 날", "EX 나이트메어"), Raid("최후의 날", "나이트메어")).Shown!.Difficulty);

    [Fact]
    public void A_tie_keeps_the_name_shown()
    {
        var s = Feed(Raid("대지를 부수는 업화의 궤적"), Raid("대지를 부수는 업학의 궤적"), Raid("대지를 부수는 업화의 궤적"), Raid("대지를 부수는 업학의 궤적"));
        Assert.Equal("대지를 부수는 업화의 궤적", s.Shown!.Title);
    }
}
