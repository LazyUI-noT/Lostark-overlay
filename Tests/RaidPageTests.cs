using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 → 레이드 (settings-reorg.plan.md 8, 11, 12): the one 대난투 체력 switch and its migration from the per-raid ones,
/// and the difficulty capsule's colour and raid logo.</summary>
public class RaidPageTests
{
    static readonly RaidGate[] Gates =
    [
        new("belgardin-g1", "벨가르딘", "1관문", [], [], Raid.없음, Tab: "그림자 레이드"),
        new("serka-g1", "고통의 마녀", "1관문", [], [], Raid.없음, Tab: "그림자 레이드"),
        new("act4-g1", "4막: 파멸의 성채", "1관문", [], [], Raid.없음, Tab: "카제로스 레이드"),
    ];

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "벨가르딘" }, true)]   // one raid's was on: the one switch is on
    [InlineData(new[] { "벨가르딘", "고통의 마녀" }, false)]
    public void An_older_file_keeps_its_meaning(string[] off, bool on)
    {
        var s = new RaidSettings { ShadowHpOff = [.. off] };
        Assert.True(s.MigrateShadowHp(Gates));
        Assert.Equal(on, s.ShadowHp);
        Assert.All(Gates.Where(g => g.IsShadow), g => Assert.Equal(on, s.ShadowHpOn(g.Raid)));
        Assert.False(s.MigrateShadowHp(Gates));   // once
    }

    [Fact]
    public void The_switch_writes_the_old_list_too_for_older_versions()
    {
        var s = new RaidSettings();
        s.SetShadowHp(false, Gates);
        Assert.Equal(["벨가르딘", "고통의 마녀"], s.ShadowHpOff);
        s.SetShadowHp(true, Gates);
        Assert.Empty(s.ShadowHpOff);
    }

    [Fact]
    public void Off_stays_off_for_a_raid_added_later()
    {
        var s = new RaidSettings();
        s.SetShadowHp(false, Gates);
        Assert.False(s.ShadowHpOn("새 그림자 레이드"));
        Assert.False(s.MigrateShadowHp([.. Gates, new("new-g1", "새 그림자 레이드", "1관문", [], [], Raid.없음, Tab: "그림자 레이드")]));
    }

    [Fact]
    public void Without_raid_data_the_switch_stays_unmigrated_and_on()
    {
        var s = new RaidSettings();
        Assert.False(s.MigrateShadowHp([]));
        Assert.True(s.ShadowHpAll([]));
    }

    [Theory]
    [InlineData("노말", DifficultyTier.Normal)]
    [InlineData("매칭", DifficultyTier.Matching)]
    [InlineData("1단계", DifficultyTier.Step1)]
    [InlineData("2단계", DifficultyTier.Step2)]
    [InlineData("3단계", DifficultyTier.Step3)]
    [InlineData("4단계", DifficultyTier.Normal)]
    [InlineData("싱글", DifficultyTier.Single)]
    [InlineData("하드", DifficultyTier.Hard)]
    [InlineData("EX 하드", DifficultyTier.Hard)]
    [InlineData("나이트메어", DifficultyTier.Nightmare)]
    [InlineData("EX 나이트메어", DifficultyTier.Nightmare)]
    [InlineData("헬", DifficultyTier.Unknown)]
    public void A_difficulty_has_its_tier(string difficulty, DifficultyTier tier) =>
        Assert.Equal(tier, RaidBadge.Tier(difficulty));

    [Theory]
    [InlineData("벨가르딘", "belgardin")]
    [InlineData("벨가르딘 · 2관문", "belgardin")]
    [InlineData("1막", "act1")]
    [InlineData("2막", "act2")]
    [InlineData("3막 · 3관문", "act3")]
    [InlineData("종막: 최후의 날", "final")]
    [InlineData("세르카", "serka")]
    [InlineData("고통의 마녀 · 2관문", "serka")]
    [InlineData("구원의 종탑", "belltower")]
    [InlineData("구원의 종탑 · 1관문", "belltower")]
    [InlineData("4막: 파멸의 성채", "act4")]
    [InlineData("4막 · 2관문", "act4")]
    [InlineData("서막 에키드나", null)]
    [InlineData(null, null)]
    public void A_raid_has_its_logo(string? raid, string? logo) => Assert.Equal(logo, RaidBadge.Logo(raid));
}
