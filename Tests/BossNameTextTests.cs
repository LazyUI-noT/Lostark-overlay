using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>자동 인식 by the boss's name as Windows OCR reads it: the gate whose boss name the text holds, a misread letter or a
/// dropped comma forgiven, a name two gates share in part (아르세노스) not enough.</summary>
public class BossNameTextTests
{
    static readonly IReadOnlyList<RaidGate> Gates = RaidData.Load(Path.Combine(Helpers.RepoRoot(), "Profiles"));

    [Theory]
    [InlineData("광신의 인도자, 아르세노스", "belltower-g2")]
    [InlineData("광신의 인도자 아르세노스", "belltower-g2")]        // the comma not read
    [InlineData("광신의 언도자, 아르세노스", "belltower-g2")]       // one letter misread
    [InlineData("보스 광신의 인도자, 아르세노스", "belltower-g2")]  // the red 보스 tag in the strip
    [InlineData("대주교, 아르세노스", "belltower-g1")]
    [InlineData("욕망의 주인, 에키드나", "act4-g1")]
    [InlineData("심연의 징벌자, 모르둠", "act3-g3")]
    public void The_boss_name_picks_its_gate(string ocr, string gate) =>
        Assert.Equal(gate, BossNameText.Match([ocr], Gates));

    [Theory]
    [InlineData("아르세노스")]           // in two gates' names
    [InlineData("모든 면역")]
    [InlineData("")]
    public void Too_little_or_another_text_matches_nothing(string ocr) => Assert.Null(BossNameText.Match([ocr], Gates));

    [Fact]
    public void A_boss_of_two_gates_needs_one_of_them_turned_on()
    {
        Assert.Null(BossNameText.Match(["대악마, 카제로스"], Gates));   // 종막 2-1 and EX 2-1
        Assert.Equal("final-ex-g2-1", BossNameText.Match(["대악마, 카제로스"], Gates, prefer: ["final-ex-g2-1"]));
        Assert.Null(BossNameText.Match(["대악마, 카제로스"], Gates, prefer: ["final-g2-1", "final-ex-g2-1"]));
    }

    [Fact]
    public void The_dungeon_panel_narrows_the_gates_to_its_raid_and_difficulty()
    {
        var ex = BossNameText.Candidates(Gates, new DungeonInfo(DungeonKind.Raid, "최후의 날", "EX 나이트메어"));
        Assert.All(ex, g => Assert.StartsWith("final-ex", g.Id));
        Assert.Equal("final-ex-g2-1", BossNameText.Match(["대악마, 카제로스"], ex));   // 2-1 and EX 2-1 told apart by the panel
        Assert.Equal(["belltower-g1", "belltower-g2"],
            BossNameText.Candidates(Gates, new DungeonInfo(DungeonKind.Raid, "구원의 종탑", null)).Select(g => g.Id));
        Assert.Equal(["act4-g1", "act4-g2"],   // the panel says 파열, raids.json 파멸; 싱글 isn't one of its difficulties
            BossNameText.Candidates(Gates, new DungeonInfo(DungeonKind.Raid, "파열의 성세", "싱글")).Select(g => g.Id));
    }

    [Fact]
    public void A_panel_naming_no_known_raid_leaves_every_gate() =>
        Assert.Equal(Gates.Count, BossNameText.Candidates(Gates, new DungeonInfo(DungeonKind.Raid, "칠흑, 폭풍의 밤", "싱글")).Count);

    [Fact]
    public void Any_line_of_the_read_may_hold_the_name() =>
        Assert.Equal("belltower-g2", BossNameText.Match(["Lv.60", "광신의 인도자, 아르세노스", "X 176"], Gates));
}
